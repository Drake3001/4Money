"""Bounded model comparisons on identical frozen PNG/OCR, two paired rounds."""

import argparse
import hashlib
import json
import random
import re
from collections import Counter
from decimal import Decimal
from pathlib import Path
from statistics import median

from extraction.analysis.receipt_images import build_vision_request, decode_vision, request_summary
from extraction.contracts import SourceFile
from extraction.providers.http import validate_api_key
from extraction.providers.openrouter import BASE_URL
from extraction.reporting import to_dict
from extraction.steps.analysis import analyzed_receipt
from extraction.steps.validate import validate_receipt
from .budget import SettledBudget
from .gemma import WORKER_DIR, load_config
from .gemma_multimodal import receipt_score
from .latency import generation_stats, stream_response, write_json
from .parsing import _restore_ocr, _summary

MODELS = {
    "gemma31": "google/gemma-4-31b-it",
    "gemma26": "google/gemma-4-26b-a4b-it",
    "gemini_lite": "google/gemini-3.1-flash-lite",
    "qwen9": "qwen/qwen3.5-9b",
}
ORIGINAL_MODELS = MODELS.copy()
BUDGET_MODELS = {
    "gemini_lite": "google/gemini-3.1-flash-lite",
    "gemini25": "google/gemini-2.5-flash-lite",
    "luna": "openai/gpt-6-luna",
    "haiku55": "anthropic/claude-haiku-5.5",
    "qwen37": "qwen/qwen3.7-flash",
}


def select_group(group):
    global MODELS
    if group not in ("original", "budget"):
        raise ValueError("Unknown model group")
    MODELS = (ORIGINAL_MODELS if group == "original" else BUDGET_MODELS).copy()


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def model_request(images, ocr, variant):
    if variant not in MODELS:
        raise ValueError("Unknown comparison model")
    payload = build_vision_request(images, ocr, routing="throughput")
    payload.update(model=MODELS[variant], stream=True, service_tier="default")
    payload["reasoning"] = {"effort": "minimal"} if variant == "gemini_lite" else {"enabled": False}
    payload["provider"]["max_price"] = {"prompt": 0.30,
        "completion": 1.60 if variant == "gemini_lite" else 0.60, "image": 0.01, "request": 0}
    if variant == "gemma31":
        payload["provider"].pop("sort", None)
        payload["provider"].update(only=["friendli"], allow_fallbacks=False)
    if variant in ("gemini25", "luna", "haiku55", "qwen37"):
        ceilings = {"gemini25": (0.12, 0.50), "luna": (0.12, 0.60),
                    "haiku55": (0.12, 0.60), "qwen37": (0.04, 0.18)}
        input_price, output_price = ceilings[variant]
        payload["provider"]["max_price"].update(prompt=input_price, completion=output_price)
    if variant in ("luna", "haiku55"):
        # These models do not expose temperature in their supported parameters.
        del payload["temperature"]
        payload["reasoning"] = ({"effort": "none"} if variant == "luna"
                                else {"enabled": False, "effort": "low"})
    return payload


def reservation(payload, catalog, image_count):
    """Reserve full model context plus maximum output and possible flat image fees."""
    model = catalog["models"][payload["model"]]
    if "image" not in model["architecture"]["input_modalities"]:
        raise ValueError("Model does not support images")
    prices = payload["provider"]["max_price"]
    return (Decimal(model["context_length"]) * Decimal(str(prices["prompt"]))
            + Decimal(payload["max_tokens"]) * Decimal(str(prices["completion"]))) / 1_000_000 + Decimal(str(prices["image"])) * image_count


def inputs(root, source):
    folder = root / source
    ocr = _restore_ocr(json.loads((folder / "ocr.json").read_text(encoding="utf-8")))
    images = [SourceFile((folder / f"page-{p.page_number}.png").read_bytes(), "image/png", "page.png") for p in ocr.pages]
    return images, ocr


def prepare(root, previous):
    if (root / "plan.json").exists() or (root / "budget.json").exists():
        raise FileExistsError("Preserve the existing study")
    catalog = json.loads((root / "catalog.json").read_text(encoding="utf-8"))
    old_plan = json.loads((previous / "plan.json").read_text(encoding="utf-8"))
    cases, schedule = [], []
    for case in old_plan["cases"]:
        source = case["source"]
        folder, old = root / source, previous / source
        folder.mkdir()
        for name in ("ocr.json", "reference.json"):
            (folder / name).write_bytes((old / name).read_bytes())
        raw = json.loads((folder / "ocr.json").read_text(encoding="utf-8"))
        for page in raw["pages"]:
            name = f"page-{page['page_number']}.png"
            if digest(old / name) != case["image_sha256"][page["page_number"] - 1]:
                raise ValueError("Original image changed")
            (folder / name).write_bytes((old / name).read_bytes())
        images, ocr = inputs(root, source)
        for variant in MODELS:
            payload = model_request(images, ocr, variant)
            bound = reservation(payload, catalog, len(images))
            if bound > Decimal("0.50"):
                raise ValueError("Single-call reservation exceeds study budget")
            write_json(folder / f"{variant}-request-summary.json", request_summary(payload))
        cases.append({"source": source, "local_seconds": case["local_seconds"],
            "image_sha256": case["image_sha256"], "ocr_sha256": digest(folder / "ocr.json"),
            "reference_sha256": digest(folder / "reference.json")})
    # Each round covers every pair once; shuffle within rounds to distribute temporal drift.
    rng = random.Random(20261026)
    for repeat in (1, 2):
        pairs = [{"source": c["source"], "variant": v, "repeat": repeat} for c in cases for v in MODELS]
        rng.shuffle(pairs)
        schedule.extend(pairs)
    write_json(root / "plan.json", {"cases": cases, "schedule": schedule, "models": MODELS,
        "catalog_sha256": digest(root / "catalog.json"), "max_calls": len(schedule), "budget_usd": "0.50",
        "policy": "Same full-resolution PNG, OCR, prompt, output schema, max_tokens 8192. Temperature 0 where supported; omitted for Luna/Haiku. No structured-output extra parameter.",
        "reasoning_policy": "Disabled for Gemma/Qwen/Gemini2.5; minimal for Gemini3.1; none for Luna; disabled with low effort for Haiku.",
        "routing_policy": "Standard tier; Gemma31 Friendli pinned if present; other models throughput with provider fallbacks. Provider is part of practical comparison.",
        "selection_policy": "Report every attempt, names/categories, exact numbers, missing/excess entries, null fills, review flags and speed; no auto-deployment.",
        "limitations": ["Three familiar development examples, assistant silver references, no independent human gold labels.",
            "Two attempts per pair, not enough for p95 or generalization; repeated prompts can use provider cache.",
            "Model tokenizers differ; compare total wall time and field quality rather than raw token counts alone.",
            "Local times are reused from previous measurement; sums are estimates, not full HTTP timings.",
            "No automatic retries; interrupted/failed attempts remain counted. Production unchanged."]})
    SettledBudget(root, "0.50", len(schedule))
    print(f"PREPARED {root}; {len(schedule)} calls; budget=0.50 USD", flush=True)


def execute(root, variants=None):
    config = load_config()
    validate_api_key(config["api_key"], "OPENROUTER_API_KEY")
    if config["base_url"].rstrip("/") != BASE_URL:
        raise ValueError("Only official OpenRouter allowed")
    plan = json.loads((root / "plan.json").read_text(encoding="utf-8"))
    if plan["models"] != MODELS:
        raise ValueError("Choose the model group matching the frozen plan")
    if digest(root / "catalog.json") != plan["catalog_sha256"]:
        raise ValueError("Frozen catalog changed")
    catalog = json.loads((root / "catalog.json").read_text(encoding="utf-8"))
    budget = SettledBudget.restore(root)
    for pair in plan["schedule"]:
        source, variant, repeat = pair["source"], pair["variant"], pair["repeat"]
        if variants is not None and variant not in variants:
            continue
        folder = root / source
        path = folder / f"{variant}-{repeat}-result.json"
        if path.exists():
            continue
        case = next(c for c in plan["cases"] if c["source"] == source)
        if digest(folder / "ocr.json") != case["ocr_sha256"] or digest(folder / "reference.json") != case["reference_sha256"]:
            raise ValueError("Frozen reference/OCR changed")
        images, ocr = inputs(root, source)
        if [hashlib.sha256(i.content).hexdigest() for i in images] != case["image_sha256"]:
            raise ValueError("Frozen images changed")
        payload = model_request(images, ocr, variant)
        saved = json.loads((folder / f"{variant}-request-summary.json").read_text(encoding="utf-8"))
        if saved != request_summary(payload):
            raise ValueError("Request differs from frozen plan")
        budget.reserve(reservation(payload, catalog, len(images)))
        record = {**pair, "status": "started", "attempt": budget.calls, "model": MODELS[variant]}
        write_json(path, record)
        print(f"START attempt={budget.calls} {source} {variant} repeat={repeat}", flush=True)
        over_bound = False
        try:
            response, timing = stream_response(payload, config["api_key"])
            write_json(folder / f"{variant}-{repeat}-response.json", response)
            try:
                budget.charge(response)
            except RuntimeError:
                over_bound = True
                raise
            record.update(timing=timing, provider=response.get("provider"), usage=response.get("usage"),
                generation_id=response.get("id"), estimated_pipeline_seconds=case["local_seconds"] + timing["total_seconds"])
            choice = response["choices"][0]
            if choice["finish_reason"] != "stop" or not timing["stream_done"]:
                raise ValueError("Incomplete generation")
            text = choice["message"]["content"]
            match = re.fullmatch(r"\s*```(?:json)?\s*\n(.*?)\n```\s*", text, re.DOTALL)
            parsed, enrichment, evidence = decode_vision(json.loads(match[1] if match else text), len(images))
            analysis = analyzed_receipt(parsed.output, enrichment, evidence)
            reference = json.loads((folder / "reference.json").read_text(encoding="utf-8"))
            record.update(status="completed", analysis=to_dict(analysis), score=receipt_score(reference, analysis),
                validation=to_dict(validate_receipt(parsed.output)), warnings=to_dict(parsed.warnings))
        except Exception as exc:
            # Transport errors expose no provider response bodies or secrets.
            record.update(status="failed", error_type=type(exc).__name__)
            if getattr(exc, "response", None) is not None:
                partial = exc.response
                write_json(folder / f"{variant}-{repeat}-partial-response.json", partial)
                record.update(timing=exc.timing, generation_id=partial.get("id"),
                              provider=partial.get("provider"), usage=partial.get("usage"),
                              provider_error_code=getattr(exc, "code", None))
                # Settle only actual usage evidence; no assumption that a failed call was free.
                if (partial.get("usage") or {}).get("cost") is not None:
                    budget.charge(partial)
            message = str(exc)
            if message.startswith("OpenRouter HTTP "):
                record["transport_error"] = message
        write_json(path, record)
        print(json.dumps({k: record.get(k) for k in ("source", "variant", "repeat", "status", "provider", "timing", "usage", "error_type", "transport_error")}), flush=True)
        if over_bound:
            raise RuntimeError("Budget bound exceeded; study halted")


def summarize(root):
    plan = json.loads((root / "plan.json").read_text(encoding="utf-8"))
    if plan["models"] != MODELS:
        raise ValueError("Choose the model group matching the frozen plan")
    records = [json.loads(p.read_text(encoding="utf-8")) for p in sorted(root.glob("*/*-result.json"))]
    summaries = {}
    for variant in MODELS:
        selected = [r for r in records if r["variant"] == variant]
        complete = [r for r in selected if r["status"] == "completed"]
        timed = [r for r in selected if r.get("timing", {}).get("total_seconds") is not None]
        counts = Counter()
        evaluations = []
        for case in plan["cases"]:
            reference = json.loads((root / case["source"] / "reference.json").read_text(encoding="utf-8"))
            for repeat in (1, 2):
                record = next((r for r in selected if r["source"] == case["source"] and r["repeat"] == repeat), None)
                if record is None:
                    continue
                score = record["score"] if record.get("status") == "completed" else receipt_score(reference)
                counts.update(score["counts"])
                evaluations.append(score["evaluation"])
        summaries[variant] = {"model": MODELS[variant], "attempted": len(selected), "completed": len(complete),
            "counts": dict(counts), "fields": _summary(evaluations),
            "provider_counts": dict(Counter(r.get("provider", "unknown") for r in selected)),
            "median_model_seconds": median(r["timing"]["total_seconds"] for r in timed) if timed else None,
            "median_accepted_model_seconds": median(r["timing"]["total_seconds"] for r in complete) if complete else None,
            "reported_cost_usd": str(sum((Decimal(str((r.get("usage") or {}).get("cost", 0))) for r in selected), Decimal(0)))}
    budget = json.loads((root / "budget.json").read_text(encoding="utf-8"))
    unknown_attempts = [a["number"] for a in budget["attempts"] if a["status"] != "settled"]
    for variant, summary in summaries.items():
        summary["unknown_cost_attempts"] = [r["attempt"] for r in records
            if r["variant"] == variant and r["attempt"] in unknown_attempts]
    write_json(root / "summary.json", {"plan": plan, "budget": budget, "models": summaries})
    # Existing field-level audit also lists reference-null fills and unflagged errors.
    from .gemma_multimodal import audit_study
    audit_cases = []
    contract_failures = []
    for case in plan["cases"]:
        reference = json.loads((root / case["source"] / "reference.json").read_text(encoding="utf-8"))
        for repeat in (1, 2):
            entry = {"source": f"{case['source']}::repeat{repeat}", "reference": reference}
            for variant in MODELS:
                record = next((r for r in records if r["source"] == case["source"] and r["variant"] == variant and r["repeat"] == repeat), None)
                entry[variant] = record or {"status": "not_run"}
                if record and record["status"] == "failed":
                    path = root / case["source"] / f"{variant}-{repeat}-response.json"
                    reason = record.get("transport_error", record.get("error_type"))
                    if path.exists():
                        try:
                            response = json.loads(path.read_text(encoding="utf-8"))
                            text = response["choices"][0]["message"]["content"]
                            match = re.fullmatch(r"\s*```(?:json)?\s*\n(.*?)\n```\s*", text, re.DOTALL)
                            decode_vision(json.loads(match[1] if match else text), len(case["image_sha256"]))
                        except Exception as exc:
                            reason = str(exc)
                    contract_failures.append({"source": case["source"], "repeat": repeat, "variant": variant, "reason": reason})
            audit_cases.append(entry)
    audit = audit_study({"methods": list(MODELS), "cases": audit_cases})
    audit["contract_failures"] = contract_failures
    audit["note"] = "Supplementary field audit; strict-contract failures remain failures in the primary summary. No predictions repaired."
    write_json(root / "quality-audit.json", audit)
    category_coverage = Counter(i["category"] for c in plan["cases"]
        for i in json.loads((root / c["source"] / "reference.json").read_text(encoding="utf-8"))["items"])
    write_json(root / "category-coverage.json", dict(category_coverage))
    lines = ["# Porównanie modeli — 26.10.2026", "", "Identyczne PNG, OCR i prompt; dwa przebiegi. Czasy bez lokalnego OCR.", "",
        "| Dokument | Model | Przebieg 1 s | Przebieg 2 s | Mediana s |", "|---|---|---:|---:|---:|"]
    for case in plan["cases"]:
        for variant in MODELS:
            values, cells = [], []
            for repeat in (1, 2):
                r = next((r for r in records if r["source"] == case["source"] and r["variant"] == variant and r["repeat"] == repeat), None)
                if r and r.get("timing", {}).get("total_seconds") is not None:
                    values.append(r["timing"]["total_seconds"])
                    cells.append(f"{values[-1]:.2f}" + (" (odrzucony)" if r["status"] != "completed" else ""))
                else:
                    cells.append(r["status"] if r else "brak")
            lines.append(f"| {case['source']} | {variant} | " + " | ".join(cells) + f" | {median(values):.2f} |" if values else
                         f"| {case['source']} | {variant} | " + " | ".join(cells) + " | — |")
    lines += ["", "| Model | Ukończone | Nazwy | Kategorie | Braki / nadmiar produktów | Koszt USD |",
              "|---|---:|---:|---:|---:|---:|"]
    for variant, s in summaries.items():
        c = s["counts"]
        lines.append(f"| {variant} | {s['completed']}/{s['attempted']} | {c.get('names_vat_normalized_correct', 0)}/{c.get('names_expected', 0)} | "
            f"{c.get('categories_primary_correct', 0)}/{c.get('categories_expected', 0)} | {c.get('items_missing', 0)} / {c.get('items_excess', 0)} | {s['reported_cost_usd']} |")
    lines += ["", "Nazwy i kategorie liczone dla 25 pozycji × 2 przebiegi, nie 50 niezależnych produktów.",
        "Referencje tego zestawu obejmują wyłącznie kategorię żywność i napoje; wynik nie ocenia pozostałych 17 kategorii.",
        "Błędy i nieukończone próby pozostają w mianownikach ocenionych prób. Szczegółowe liczby, brakujące pola i walidacja w summary.json oraz raportach przypadków.",
        "Czasy obejmują także odebrane, ale odrzucone odpowiedzi. quality-audit.json wyjaśnia błędy kontraktu i pól.",
        "Koszt API: " + budget["reported_cost_usd"] + " USD; limit 0,50 USD; próby " + str(budget["calls"]) + "/" + str(plan["max_calls"]) + ".", "",
        "Koszt powyżej obejmuje tylko znane opłaty. Próby bez potwierdzonego kosztu: " + str(unknown_attempts)
        + "; zachowana rezerwacja łącznie " + budget["reserved_usd"] + " USD.", "",
        *["- " + s for s in plan["limitations"]], ""]
    lines += ["## Pola liczbowe i kompletność", "",
        "| Model | Dokładne podstawowe pola finansowe dokumentu | Błędne znane liczby | Brakujące znane liczby | Uzupełnienia przy nullu referencji |",
        "|---|---:|---:|---:|---:|"]
    for variant in MODELS:
        a = audit["methods"][variant]
        lines.append(f"| {variant} | {len(a['core_financial_exact_documents'])}/6 | {len(a['different_known_numbers'])} | "
                     f"{len(a['missing_known_numbers'])} | {len(a['reference_null_fills'])} |")
    lines += ["", "Podstawowe pola: dopasowane produkty bez braków/nadmiaru, ilości, ceny jednostkowe, kwoty pozycji, kwoty kaucji/zwrotów i sumy.",
        "Nie jest to liczba całkowicie bezbłędnych paragonów: nie obejmuje nazw, dat, kategorii ani wszystkich pól rabatów.",
        "Błędne/brakujące liczby w tabeli dotyczą tylko przyjętych odpowiedzi; odrzucone dokumenty pozostają oddzielnie jako niepowodzenia.",
        "Braki before_discount i discount=0 na pozycjach bez rabatu nie są utożsamiane z błędnymi odczytanymi kwotami.", ""]
    (root / "report.md").write_text("\n".join(lines), encoding="utf-8")


def reconcile(root):
    """Read billing metadata for failed streams; run only when execution is idle."""
    config = load_config()
    validate_api_key(config["api_key"], "OPENROUTER_API_KEY")
    budget = SettledBudget.restore(root)
    entries = []
    for path in sorted(root.glob("*/*-result.json")):
        record = json.loads(path.read_text(encoding="utf-8"))
        identifier = record.get("generation_id")
        if record["status"] != "failed" or not identifier:
            continue
        attempt = budget.attempts[record["attempt"] - 1]
        if attempt["status"] == "settled":
            continue
        stats = generation_stats(identifier, config["api_key"])
        if stats.get("id") != identifier or stats.get("total_cost") is None:
            entries.append({"generation_id": identifier, "status": "metadata_unavailable"})
            continue
        cost = Decimal(str(stats["total_cost"]))
        if not cost.is_finite() or not 0 <= cost <= Decimal(attempt["bound"]):
            raise RuntimeError("Invalid or above-bound reconciled cost; study halted")
        # Keep the original reservation and evidence: only a returned billed amount releases it.
        attempt.update(status="settled", cost=str(cost), evidence="generation_metadata", generation_id=identifier)
        budget.persist()
        record.update(billing_metadata=stats, usage={**(record.get("usage") or {}), "cost": float(cost)},
                      billing_reconciled=True)
        write_json(path, record)
        entries.append({"generation_id": identifier, "status": "settled", "cost": str(cost),
                        "attempt": record["attempt"]})
    write_json(root / "billing-reconciliation.json", {"entries": entries,
        "reserved_usd": str(budget.reserved), "reported_cost_usd": str(budget.cost)})
    print(json.dumps({"entries": entries, "reserved_usd": str(budget.reserved)}), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--group", choices=("original", "budget"), default="original")
    parser.add_argument("--previous", type=Path, default=WORKER_DIR / "data/output/latency-2026-10-26-v3")
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--variants", nargs="+", help="Execute only selected planned variants, preserving existing attempts and budget")
    parser.add_argument("--summarize", action="store_true")
    parser.add_argument("--reconcile", action="store_true", help="Read generation billing metadata only; execution must be idle")
    args = parser.parse_args()
    if args.reconcile and args.execute:
        parser.error("Reconcile separately, after execution has ended")
    select_group(args.group)
    if args.prepare:
        prepare(args.root, args.previous)
    if args.execute:
        if args.variants is not None and not set(args.variants) <= set(MODELS):
            parser.error("Variant is not in the selected model group")
        execute(args.root, args.variants)
    if args.summarize:
        summarize(args.root)
    if args.reconcile:
        reconcile(args.root)


if __name__ == "__main__":
    main()
