"""Bounded latency study; production workflows and prompts remain unchanged.

Run preparation first, then --execute. Repeated execution reuses the persisted budget
and completed attempts; failed calls are never retried automatically.
"""

import argparse
import copy
import hashlib
import json
import re
from pathlib import Path
from time import perf_counter
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import Request, build_opener

from extraction.analysis.receipt_images import (
    OCR_GUIDANCE, build_vision_request, decode_vision, request_summary, vision_bound,
)
from extraction.pipeline import Pipeline
from extraction.providers.http import NoRedirect, validate_api_key
from extraction.providers.openrouter import BASE_URL
from extraction.reporting import to_dict
from extraction.runner import read_sources
from extraction.steps.analysis import analyzed_receipt
from extraction.steps.images import page_sources
from extraction.steps.ocr import RapidOCRStep
from extraction.steps.orientation import OrientationStep
from extraction.steps.prepare import PrepareDocumentStep
from extraction.steps.validate import validate_receipt
from .budget import SettledBudget
from .gemma import WORKER_DIR, load_config
from .gemma_multimodal import receipt_score
from .parsing import _restore_ocr

SOURCES = ("Screenshot_20261011_140209_appka.jpg", "IMG_6799.JPEG", "20261018_090822.jpg")
VARIANTS = ("baseline", "throughput", "compact")
EXTRA_VARIANTS = ("no_thinking", "latency", "throughput_nonstream")


class StudyStreamError(RuntimeError):
    """Retain partial generation/usage for billing reconciliation, never print bodies."""

    def __init__(self, response, timing, code):
        super().__init__("Provider returned an SSE error")
        self.response, self.timing, self.code = response, timing, code


def write_json(path, value):
    """Study records are updated; the general reporting writer is create-only."""
    path = Path(path)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def variant_request(base, variant):
    """Compact preserves every OCR value, including quadrilaterals and IDs."""
    if variant not in (*VARIANTS, *EXTRA_VARIANTS):
        raise ValueError("Unknown latency variant")
    payload = copy.deepcopy(base)
    payload["stream"] = True
    if variant == "throughput_nonstream":
        payload["stream"] = False
    if variant != "baseline":
        payload["provider"].pop("order", None)
        payload["provider"]["sort"] = "latency" if variant == "latency" else "throughput"
    if variant == "no_thinking":
        payload["reasoning"] = {"enabled": False}
    if variant == "compact":
        text = payload["messages"][1]["content"][-1]
        pages = json.loads(text["text"][len(OCR_GUIDANCE):])
        rows = [{"page": page["page"], "fragments": [
            [f["id"], f["text"], f["confidence"], f["box"]] for f in page["fragments"]
        ]} for page in pages]
        text["text"] = (OCR_GUIDANCE + "\nKażdy fragment to tablica [id, tekst, confidence, box].\n"
                        + json.dumps(rows, ensure_ascii=False, separators=(",", ":")))
    return payload


def stream_response(payload, api_key, *, timeout=180):
    """Collect one SSE response and measure actual content/reasoning arrival."""
    body = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    request = Request(BASE_URL + "/chat/completions", data=body, method="POST", headers={
        "Content-Type": "application/json", "Authorization": "Bearer " + api_key,
    })
    start = perf_counter()
    response = {"choices": [{"message": {"content": ""}, "finish_reason": None}]}
    timing = {"request_bytes": len(body), "first_event_seconds": None,
              "first_content_seconds": None, "first_reasoning_seconds": None}
    content, reasoning_chars, done = [], 0, False
    try:
        with build_opener(NoRedirect()).open(request, timeout=timeout) as stream:
            timing["headers_seconds"] = perf_counter() - start
            for line in stream:
                if perf_counter() - start > timeout:
                    raise TimeoutError("Overall experiment call deadline exceeded")
                if not line.startswith(b"data:"):
                    continue
                value = line[5:].strip()
                if value == b"[DONE]":
                    done = True
                    break
                event = json.loads(value)
                now = perf_counter() - start
                if timing["first_event_seconds"] is None:
                    timing["first_event_seconds"] = now
                for key in ("id", "model", "provider", "usage"):
                    if event.get(key) is not None:
                        response[key] = event[key]
                if event.get("error"):
                    error = event["error"]
                    # Messages can contain provider credentials: keep only code and a redacted message.
                    message = str(error.get("message", "")) if isinstance(error, dict) else ""
                    message = message.replace(api_key, "<redacted>")
                    message = re.sub(r"sk-[A-Za-z0-9_-]+", "<redacted>", message)
                    code = error.get("code") if isinstance(error, dict) else None
                    response["error"] = {"code": code, "message": message[:400]}
                    response["choices"][0]["message"]["content"] = "".join(content)
                    timing.update(total_seconds=now, reasoning_characters=reasoning_chars, stream_done=False)
                    raise StudyStreamError(response, timing, code)
                for choice in event.get("choices", []):
                    if choice.get("index", 0) != 0:
                        raise ValueError("Unexpected multiple choices")
                    delta = choice.get("delta") or {}
                    if delta.get("content"):
                        if timing["first_content_seconds"] is None:
                            timing["first_content_seconds"] = now
                        content.append(delta["content"])
                    reasoning = delta.get("reasoning") or delta.get("reasoning_content")
                    if reasoning or delta.get("reasoning_details"):
                        if timing["first_reasoning_seconds"] is None:
                            timing["first_reasoning_seconds"] = now
                        reasoning_chars += len(reasoning or "")
                    if choice.get("finish_reason"):
                        response["choices"][0]["finish_reason"] = choice["finish_reason"]
    except HTTPError as exc:
        raise RuntimeError(f"OpenRouter HTTP {exc.code}; no retry") from None
    timing.update(total_seconds=perf_counter() - start, reasoning_characters=reasoning_chars,
                  stream_done=done)
    response["choices"][0]["message"]["content"] = "".join(content)
    # Preserve charge information even for an incomplete/truncated generation.
    return response, timing


def generation_stats(identifier, api_key):
    request = Request(BASE_URL + "/generation?" + urlencode({"id": identifier}),
                      headers={"Authorization": "Bearer " + api_key})
    try:
        with build_opener(NoRedirect()).open(request, timeout=20) as result:
            return json.load(result)["data"]
    except HTTPError as exc:
        return {"unavailable_http_status": exc.code}
    except (TimeoutError, OSError):
        return {"unavailable": True}


def prepare(root):
    if (root / "plan.json").exists() or (root / "budget.json").exists():
        raise FileExistsError("Prepared study already exists")
    references = {}
    for source in SOURCES:
        candidates = list((WORKER_DIR / "experiments/fixtures/gemma-vision-v2").rglob(f"{Path(source).stem}.expected.json"))
        if len(candidates) != 1:
            raise ValueError(f"Expected one reference for {source}")
        references[source] = candidates[0]
    root.mkdir(parents=True, exist_ok=True)
    local = Pipeline([("prepare", PrepareDocumentStep()), ("orient", OrientationStep()),
                      ("ocr", RapidOCRStep())])
    # Same synthetic warmup as the service, exercising these exact shared engines.
    from extraction.workflows import warmup_service
    start = perf_counter()
    warmup_service({"vision_ocr": local})
    warmup_seconds = perf_counter() - start
    cases = []
    for source in SOURCES:
        folder = root / source
        folder.mkdir(exist_ok=True)
        sources = read_sources((WORKER_DIR / "data/input" / source,))
        result = local.run(sources, capture_steps=True)
        if not result.ok:
            raise RuntimeError(f"Preparation failed: {result.error}")
        ocr = result.output
        write_json(folder / "ocr.json", to_dict(ocr))
        write_json(folder / "local.json", {"steps": to_dict(result.steps), "warnings": to_dict(result.warnings)})
        for page in ocr.document.pages:
            (folder / f"page-{page.number}.png").write_bytes(page.content)
        ref_path = references[source]
        write_json(folder / "reference.json", json.loads(ref_path.read_text(encoding="utf-8")))
        base = build_vision_request(page_sources(ocr.document).files, ocr)
        for variant in (*VARIANTS, *EXTRA_VARIANTS):
            write_json(folder / f"{variant}-request-summary.json", request_summary(variant_request(base, variant)))
        cases.append({"source": source, "local_seconds": sum(s.seconds for s in result.steps),
                      "image_sha256": [hashlib.sha256(p.content).hexdigest() for p in ocr.document.pages],
                      "reference_sha256": hashlib.sha256(ref_path.read_bytes()).hexdigest()})
        print(f"PREPARED {source} local_seconds={cases[-1]['local_seconds']:.3f}", flush=True)
    write_json(root / "plan.json", {"cases": cases, "variants": VARIANTS, "max_calls": 12,
        "budget_usd": "0.50", "warmup_seconds": warmup_seconds,
        "limitations": ["Three development examples; no generalization claim; assistant silver references.",
                         "All variants stream for measurement; production currently does not stream.",
                         "Local processing measured once after warmup; per-call totals reuse that measurement.",
                         "No automatic retries; production configuration unchanged."]})
    SettledBudget(root, "0.50", 12)


def execute(root, variants, sources):
    config = load_config()
    validate_api_key(config["api_key"], "OPENROUTER_API_KEY")
    if config["base_url"].rstrip("/") != BASE_URL:
        raise ValueError("Only official OpenRouter base URL allowed")
    plan = json.loads((root / "plan.json").read_text(encoding="utf-8"))
    budget = SettledBudget.restore(root)
    for case in plan["cases"]:
        source = case["source"]
        if sources and source not in sources:
            continue
        folder = root / source
        ocr = _restore_ocr(json.loads((folder / "ocr.json").read_text(encoding="utf-8")))
        from extraction.contracts import SourceFile
        images = [SourceFile((folder / f"page-{p.page_number}.png").read_bytes(), "image/png", "page.png") for p in ocr.pages]
        base = build_vision_request(images, ocr)
        reference = json.loads((folder / "reference.json").read_text(encoding="utf-8"))
        for variant in variants:
            path = folder / f"{variant}-result.json"
            if path.exists():
                continue
            payload = variant_request(base, variant)
            # Persist intent before sending: interrupted calls remain counted and never auto-retry.
            budget.reserve(vision_bound())
            record = {"source": source, "variant": variant, "status": "started", "local_seconds": case["local_seconds"]}
            write_json(path, record)
            print(f"START {source} {variant} attempt={budget.calls}", flush=True)
            try:
                if payload["stream"]:
                    response, timing = stream_response(payload, config["api_key"])
                else:
                    from extraction.providers import OpenRouterClient
                    body = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
                    started = perf_counter()
                    response = OpenRouterClient(config["api_key"]).send(body, timeout=180)
                    timing = {"total_seconds": perf_counter() - started, "request_bytes": len(body),
                              "first_content_seconds": None, "stream_done": True,
                              "mode": "nonstream_no_arrival_measurement"}
                write_json(folder / f"{variant}-response.json", response)
                budget.charge(response)
                record.update(timing=timing, provider=response.get("provider"), usage=response.get("usage"),
                              generation_id=response.get("id"),
                              estimated_pipeline_seconds=case["local_seconds"] + timing["total_seconds"])
                if response.get("id"):
                    record["generation_stats"] = generation_stats(response["id"], config["api_key"])
                choice = response["choices"][0]
                if choice["finish_reason"] != "stop" or not timing["stream_done"]:
                    raise ValueError("Incomplete stream or non-stop finish")
                # Same fenced-JSON handling as the production analyzer.
                import re
                text = choice["message"]["content"]
                match = re.fullmatch(r"\s*```(?:json)?\s*\n(.*?)\n```\s*", text, re.DOTALL)
                parsed, enrichment, evidence = decode_vision(json.loads(match[1] if match else text), len(images))
                analysis = analyzed_receipt(parsed.output, enrichment, evidence)
                record.update(status="completed", analysis=to_dict(analysis),
                              score=receipt_score(reference, analysis),
                              warnings=to_dict(parsed.warnings), validation=to_dict(validate_receipt(parsed.output)))
            except Exception as exc:
                # Do not print provider bodies or credentials.
                record.update(status="failed", error_type=type(exc).__name__)
            write_json(path, record)
            print(json.dumps({k: record.get(k) for k in ("source", "variant", "status", "timing", "provider", "usage", "error_type")}), flush=True)


def summarize(root):
    """Offline report, including failures and quality changes, without new calls."""
    from collections import Counter
    from statistics import median
    from .parsing import _summary

    plan = json.loads((root / "plan.json").read_text(encoding="utf-8"))
    budget = json.loads((root / "budget.json").read_text(encoding="utf-8"))
    variants = (*VARIANTS, "throughput_nonstream")
    records = [json.loads(path.read_text(encoding="utf-8")) for path in sorted(root.glob("*/*-result.json"))]
    summary = {}
    for variant in variants:
        selected = [r for r in records if r["variant"] == variant]
        complete = [r for r in selected if r["status"] == "completed"]
        counts = Counter()
        for r in complete:
            counts.update(r["score"]["counts"])
        summary[variant] = {"attempted": len(selected), "completed": len(complete), "counts": dict(counts),
            "fields": _summary([r["score"]["evaluation"] for r in complete]),
            "median_model_seconds": median(r["timing"]["total_seconds"] for r in complete) if complete else None}
    write_json(root / "summary.json", {"budget": budget, "variants": summary,
        "limitations": plan["limitations"] + ["Nonstream repeats can benefit from provider prompt caching; inspect usage.",
            "Quality includes exact transcription; no independent human reference validation.",
            "No lossless-quality or ten-second guarantee. All settings remain opt-in."]})
    lines = ["# Eksperyment wydajności workera — 26.10.2026", "",
        "Trzy lokalne paragony, identyczne przygotowane PNG i OCR. Model i główny prompt bez zmian.",
        "12 płatnych wywołań; koszt API " + budget["reported_cost_usd"] + " USD; limit 0,50 USD.", "",
        "| Dokument | Lokalne przygotowanie + OCR | DeepInfra, stream | Throughput, stream | Zwarty OCR, stream | Throughput, bez stream |",
        "|---|---:|---:|---:|---:|---:|"]
    for case in plan["cases"]:
        cells = []
        for variant in variants:
            record = next((r for r in records if r["source"] == case["source"] and r["variant"] == variant), {})
            cells.append(f"{record['timing']['total_seconds']:.2f} s" if record.get("status") == "completed" else record.get("status", "brak"))
        lines.append(f"| {case['source']} | {case['local_seconds']:.2f} s | " + " | ".join(cells) + " |")
    lines += ["", "Czasy modelu obejmują żądanie i odebranie kompletnej odpowiedzi. Nie obejmują lokalnego OCR.",
        "Suma lokalnego pomiaru i czasu modelu jest oszacowaniem pipeline’u, a nie pomiarem pełnego HTTP.", "",
        "| Wariant | Ukończone | Nazwy zgodne ze wzorcem | Kategorie | Braki / nadmiar produktów |",
        "|---|---:|---:|---:|---:|"]
    for variant, result in summary.items():
        c = result["counts"]
        lines.append(f"| {variant} | {result['completed']}/{result['attempted']} | {c.get('names_vat_normalized_correct', 0)}/25 | "
                     f"{c.get('categories_primary_correct', 0)}/25 | {c.get('items_missing', 0)} / {c.get('items_excess', 0)} |")
    lines += ["", "Szybszy routing obsłużył Friendli, baseline DeepInfra. W próbach stream reasoning_tokens=0.",
        "Na Biedronce szybszy routing pogorszył dwie literalne nazwy (Woda Gaz Żyw → Żywu oraz książ → ksiaz).",
        "Błąd rabatu cebuli (0,38 zamiast 0,22 zł po rabacie) wystąpił w baseline i obu próbach throughput.",
        "Kompaktowy OCR odczytał tę kwotę poprawnie, ale nie poprawił konsekwentnie czasu ani nazw.",
        "Różnice before_discount i jednostek oraz braki pól pozostają widoczne w summary.json i raportach przypadków.",
        "Brak ostrzeżeń walidatora nie dowodzi poprawności. Nie wdrożono kompaktowania ani zmiany rozdzielczości.", "",
        "Włączenie szybszego routingu w API: EXTRACTION_VISION_ROUTING=throughput i restart usługi.",
        "Domyślnie pozostaje deepinfra. Model, prompt, limit ceny, OCR i kontrakt odpowiedzi pozostają te same.", "",
        "Próba ma charakter rozwojowy: trzy znane dokumenty i wzorce asystenta; nie dowodzi braku regresji.",
        "Warianty uruchomiono sekwencyjnie w stałej kolejności, bez randomizacji. Trzy ostatnie próby mają raportowane cached_tokens.",
        "Nie wyznaczamy p95 ani stabilnej mediany dla każdego dokumentu na podstawie jednej/dwóch prób.",
        "Cel 10 s dla dowolnego paragonu nie został osiągnięty. Krótki paragon po rozgrzaniu OCR może się do niego zbliżyć.", ""]
    (root / "report.md").write_text("\n".join(lines), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--summarize", action="store_true", help="Offline report; no network")
    parser.add_argument("--variants", nargs="+", choices=(*VARIANTS, *EXTRA_VARIANTS), default=list(VARIANTS))
    parser.add_argument("--sources", nargs="*")
    args = parser.parse_args()
    if args.prepare:
        prepare(args.root)
    if args.execute:
        execute(args.root, args.variants, args.sources)
    if args.summarize:
        summarize(args.root)


if __name__ == "__main__":
    main()
