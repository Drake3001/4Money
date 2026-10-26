"""Paired image transport/output-format study on frozen OCR; never changes API defaults."""

import argparse
import hashlib
import json
import random
import re
from collections import Counter
from io import BytesIO
from pathlib import Path
from statistics import median
from time import perf_counter

from PIL import Image

from extraction.analysis.receipt_images import build_vision_request, decode_vision, request_summary, vision_bound
from extraction.contracts import SourceFile
from extraction.providers.http import validate_api_key
from extraction.reporting import to_dict
from extraction.steps.analysis import analyzed_receipt
from extraction.steps.validate import validate_receipt
from .budget import SettledBudget
from .gemma import WORKER_DIR, load_config
from .gemma_multimodal import receipt_score
from .latency import stream_response, write_json
from .parsing import _restore_ocr, _summary

VARIANTS = ("png", "jpeg95", "jpeg95_2048", "jpeg95_compact_output")
COMPACT_OUTPUT = (
    "\nWymaganie formatu zapisu: zwróć JSON w jednej linii, bez wcięć i bez Markdown. "
    "Nie dodawaj spacji między elementami składni JSON. Zachowaj naturalne spacje "
    "wewnątrz nazw i innych stringów. Zachowaj wszystkie wymagane pola, również null, "
    "oraz kompletną treść wszystkich produktów i pozostałych grup."
)


def encode_image(content, variant):
    """OCR stays untouched: only the representation sent to Gemma changes."""
    if variant not in VARIANTS:
        raise ValueError("Unknown image study variant")
    start = perf_counter()
    with Image.open(BytesIO(content)) as original:
        original_size = original.size
        if variant == "png":
            return content, "image/png", {"original_size": original_size, "size": original_size,
                                          "encode_seconds": perf_counter() - start}
        with original.convert("RGB") as image:
            if variant == "jpeg95_2048":
                image.thumbnail((2048, 2048), Image.Resampling.LANCZOS)
            output = BytesIO()
            image.save(output, format="JPEG", quality=95, subsampling=0)
            return output.getvalue(), "image/jpeg", {"original_size": original_size, "size": image.size,
                                                       "encode_seconds": perf_counter() - start}


def study_request(images, ocr, variant):
    payload = build_vision_request(images, ocr, routing="throughput")
    # Hold provider constant so a routing change cannot masquerade as a format win.
    payload["provider"].pop("sort", None)
    payload["provider"].update(only=["friendli"], allow_fallbacks=False)
    payload["stream"] = True
    if variant == "jpeg95_compact_output":
        payload["messages"][0]["content"] += COMPACT_OUTPUT
    return payload


def prepare(root, previous):
    if root.exists():
        raise FileExistsError("Choose a new experiment directory")
    frozen = json.loads((previous / "plan.json").read_text(encoding="utf-8"))
    root.mkdir(parents=True)
    schedule, cases = [], []
    for case in frozen["cases"]:
        source = case["source"]
        old, folder = previous / source, root / source
        folder.mkdir()
        for name in ("ocr.json", "reference.json"):
            (folder / name).write_bytes((old / name).read_bytes())
        ocr = _restore_ocr(json.loads((folder / "ocr.json").read_text(encoding="utf-8")))
        for variant in VARIANTS:
            images, metadata = [], []
            for page in ocr.pages:
                original = (old / f"page-{page.page_number}.png").read_bytes()
                if hashlib.sha256(original).hexdigest() != case["image_sha256"][page.page_number - 1]:
                    raise ValueError("Frozen original image changed")
                content, mime, metrics = encode_image(original, variant)
                extension = "png" if mime == "image/png" else "jpg"
                name = f"{variant}-page-{page.page_number}.{extension}"
                (folder / name).write_bytes(content)
                images.append(SourceFile(content, mime, "page." + extension))
                metadata.append({**metrics, "file": name, "mime_type": mime, "bytes": len(content),
                                 "sha256": hashlib.sha256(content).hexdigest()})
            payload = study_request(images, ocr, variant)
            write_json(folder / f"{variant}-request-summary.json", request_summary(payload))
            write_json(folder / f"{variant}-images.json", metadata)
            schedule.append({"source": source, "variant": variant})
            print(json.dumps({"source": source, "variant": variant, "images": metadata}), flush=True)
        cases.append({"source": source, "original_local_seconds": case["local_seconds"],
                      "ocr_sha256": hashlib.sha256((folder / "ocr.json").read_bytes()).hexdigest(),
                      "reference_sha256": hashlib.sha256((folder / "reference.json").read_bytes()).hexdigest()})
    random.Random(20261026).shuffle(schedule)
    write_json(root / "plan.json", {"cases": cases, "schedule": schedule, "previous": str(previous),
        "max_calls": len(schedule), "budget_usd": "0.50", "provider": "friendli", "seed": 20261026,
        "variants": VARIANTS, "input_policy": "Same frozen OCR and prepared orientation; no OCR reruns.",
        "quality_policy": "Report all failures, literal names, categories, missing/excess items, numeric fields and null fills.",
        "limitations": ["Three known development documents; assistant silver references, not human gold labels.",
                         "One attempt per case/variant; no p95 or generalization claims.",
                         "JPEG is lossy, resizing changes vision input only; full-resolution OCR is identical.",
                         "Randomized fixed schedule, same provider, prompt caching still possible.",
                         "No automatic retries; interrupted calls remain counted and are not resent."]})
    SettledBudget(root, "0.50", len(schedule))


def execute(root):
    config = load_config()
    validate_api_key(config["api_key"], "OPENROUTER_API_KEY")
    from extraction.providers.openrouter import BASE_URL
    if config["base_url"].rstrip("/") != BASE_URL:
        raise ValueError("Only official OpenRouter allowed")
    budget = SettledBudget.restore(root)
    plan = json.loads((root / "plan.json").read_text(encoding="utf-8"))
    for pair in plan["schedule"]:
        source, variant = pair["source"], pair["variant"]
        folder = root / source
        path = folder / f"{variant}-result.json"
        if path.exists():
            continue
        case = next(c for c in plan["cases"] if c["source"] == source)
        for name, key in (("ocr.json", "ocr_sha256"), ("reference.json", "reference_sha256")):
            if hashlib.sha256((folder / name).read_bytes()).hexdigest() != case[key]:
                raise ValueError("Frozen experiment input changed")
        ocr = _restore_ocr(json.loads((folder / "ocr.json").read_text(encoding="utf-8")))
        metadata = json.loads((folder / f"{variant}-images.json").read_text(encoding="utf-8"))
        images = []
        for entry in metadata:
            content = (folder / entry["file"]).read_bytes()
            if hashlib.sha256(content).hexdigest() != entry["sha256"]:
                raise ValueError("Prepared image changed")
            images.append(SourceFile(content, entry["mime_type"], "page"))
        payload = study_request(images, ocr, variant)
        # Refuse mutations of the frozen request, including prompt, before charging.
        saved = json.loads((folder / f"{variant}-request-summary.json").read_text(encoding="utf-8"))
        if request_summary(payload) != saved:
            raise ValueError("Request no longer matches frozen plan")
        budget.reserve(vision_bound())
        record = {**pair, "status": "started", "images": metadata, "attempt": budget.calls}
        write_json(path, record)
        print(f"START {source} {variant} attempt={budget.calls}", flush=True)
        try:
            response, timing = stream_response(payload, config["api_key"])
            write_json(folder / f"{variant}-response.json", response)
            budget.charge(response)
            record.update(timing=timing, provider=response.get("provider"), usage=response.get("usage"),
                          generation_id=response.get("id"))
            if response.get("provider") != "Friendli":
                raise ValueError("Unexpected provider; paired study stopped")
            choice = response["choices"][0]
            if choice["finish_reason"] != "stop" or not timing["stream_done"]:
                raise ValueError("Incomplete response")
            text = choice["message"]["content"]
            record["output_format"] = {"characters": len(text), "newlines": text.count("\n"),
                                       "fenced": text.strip().startswith("```")}
            match = re.fullmatch(r"\s*```(?:json)?\s*\n(.*?)\n```\s*", text, re.DOTALL)
            parsed, enrichment, evidence = decode_vision(json.loads(match[1] if match else text), len(images))
            analysis = analyzed_receipt(parsed.output, enrichment, evidence)
            reference = json.loads((folder / "reference.json").read_text(encoding="utf-8"))
            record.update(status="completed", analysis=to_dict(analysis), score=receipt_score(reference, analysis),
                          validation=to_dict(validate_receipt(parsed.output)), warnings=to_dict(parsed.warnings))
        except Exception as exc:
            record.update(status="failed", error_type=type(exc).__name__)
        write_json(path, record)
        print(json.dumps({k: record.get(k) for k in ("source", "variant", "status", "provider", "timing", "usage", "output_format", "error_type")}), flush=True)


def summarize(root):
    plan = json.loads((root / "plan.json").read_text(encoding="utf-8"))
    records = [json.loads(p.read_text(encoding="utf-8")) for p in sorted(root.glob("*/*-result.json"))]
    summaries = {}
    for variant in VARIANTS:
        selected = [r for r in records if r["variant"] == variant]
        complete = [r for r in selected if r["status"] == "completed"]
        counts = Counter()
        for r in complete:
            counts.update(r["score"]["counts"])
        summaries[variant] = {"attempted": len(selected), "completed": len(complete), "counts": dict(counts),
            "fields": _summary([r["score"]["evaluation"] for r in complete]),
            "median_model_seconds": median(r["timing"]["total_seconds"] for r in complete) if complete else None}
    budget = json.loads((root / "budget.json").read_text(encoding="utf-8"))
    write_json(root / "summary.json", {"plan": plan, "budget": budget, "variants": summaries})
    lines = ["# Format zdjęcia i długość JSON-a — 26.10.2026", "",
        "Wszystkie wywołania przypięte do Friendli; identyczny OCR w pełnej rozdzielczości. Jedna próba na komórkę.",
        "Czas obejmuje wysłanie i odebranie odpowiedzi Gemmy, bez OCR i kodowania obrazu.", "",
        "| Dokument | Wariant | Obraz MB | Żądanie MB | Gemma s | Pierwsza treść s | Tokeny wyjścia | Nazwy | Kategorie |",
        "|---|---|---:|---:|---:|---:|---:|---:|---:|"]
    for case in plan["cases"]:
        for variant in VARIANTS:
            r = next((r for r in records if r["source"] == case["source"] and r["variant"] == variant), None)
            if not r or r["status"] != "completed":
                lines.append(f"| {case['source']} | {variant} | — | — | {r['status'] if r else 'brak'} | — | — | — | — |")
                continue
            t, c = r["timing"], r["score"]["counts"]
            lines.append(f"| {case['source']} | {variant} | {sum(i['bytes'] for i in r['images']) / 1e6:.3f} | "
                f"{t['request_bytes'] / 1e6:.3f} | {t['total_seconds']:.2f} | {t['first_content_seconds']:.2f} | "
                f"{r['usage']['completion_tokens']} | {c.get('names_vat_normalized_correct', 0)}/{c['names_expected']} | "
                f"{c.get('categories_primary_correct', 0)}/{c['categories_expected']} |")
    lines += ["", "Koszt API: " + budget["reported_cost_usd"] + " USD; próby: " + str(budget["calls"]) + "/12.", "",
              *["- " + s for s in plan["limitations"]], "",
              "Ocena szczegółowa wszystkich pól, braki i nadmiar produktów oraz błędy walidacji są w summary.json i *-result.json.",
              "Wyniki nie stanowią dowodu zachowania jakości na innych dokumentach; ustawienia produkcyjne bez zmian.", ""]
    (root / "report.md").write_text("\n".join(lines), encoding="utf-8")


def profile_ocr(root):
    """Local-only diagnostic: would resizing the OCR input help or change text?"""
    from extraction.steps.ocr import RapidOCRStep
    from difflib import SequenceMatcher

    plan = json.loads((root / "plan.json").read_text(encoding="utf-8"))
    output = root / "ocr-image-profile.json"
    if output.exists():
        raise FileExistsError("OCR profile already exists; preserve measured results")
    adapter = RapidOCRStep()
    adapter.initialize()
    engine = adapter._engine
    first = root / plan["cases"][0]["source"] / "png-page-1.png"
    engine(first.read_bytes())  # warmup excluded
    records = []
    for case in plan["cases"]:
        baseline = None
        for variant in ("png", "jpeg95", "jpeg95_2048"):
            folder = root / case["source"]
            metadata = json.loads((folder / f"{variant}-images.json").read_text(encoding="utf-8"))[0]
            content = (folder / metadata["file"]).read_bytes()
            start = perf_counter()
            raw = engine(content)
            seconds = perf_counter() - start
            texts = list(raw.txts or ())
            if baseline is None:
                baseline = texts
            baseline_numbers = Counter(re.findall(r"\d+(?:[.,]\d+)*", "\n".join(baseline)))
            numbers = Counter(re.findall(r"\d+(?:[.,]\d+)*", "\n".join(texts)))
            record = {"source": case["source"], "variant": variant, "seconds": seconds,
                "substage_seconds": raw.elapse_list, "texts": texts,
                "scores": [float(s) for s in raw.scores] if raw.scores is not None else [],
                "boxes": raw.boxes.tolist() if raw.boxes is not None else [],
                "same_text_as_png": texts == baseline,
                "text_similarity_to_png": SequenceMatcher(None, "\n".join(baseline), "\n".join(texts), autojunk=False).ratio(),
                "numeric_tokens_removed_vs_png": dict(baseline_numbers - numbers),
                "numeric_tokens_added_vs_png": dict(numbers - baseline_numbers)}
            records.append(record)
            write_json(output, {"model_version": adapter.version, "config": adapter.config,
                "limitations": ["One measurement per variant/case; no stable latency inference.",
                    "Text comparison is against PNG OCR, not ground truth; numeric token differences are diagnostic only.",
                    "Input changes affect this local diagnostic only; paid experiment used unchanged full-resolution OCR."],
                "records": records})
            print(json.dumps({k: record[k] for k in ("source", "variant", "seconds", "same_text_as_png", "text_similarity_to_png", "numeric_tokens_removed_vs_png", "numeric_tokens_added_vs_png")}), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--previous", type=Path, default=WORKER_DIR / "data/output/latency-2026-10-26-v3")
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--summarize", action="store_true")
    parser.add_argument("--ocr-profile", action="store_true", help="Local-only OCR diagnostic; no API calls")
    args = parser.parse_args()
    if args.prepare:
        prepare(args.root, args.previous)
    if args.execute:
        execute(args.root)
    if args.summarize:
        summarize(args.root)
    if args.ocr_profile:
        profile_ocr(args.root)


if __name__ == "__main__":
    main()
