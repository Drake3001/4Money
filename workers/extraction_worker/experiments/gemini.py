"""Compare parsers on saved OCR; the Gemini step lives in extraction.steps.parsing."""

import argparse
import hashlib
import json
import os
import re
from decimal import Decimal
from pathlib import Path
from time import perf_counter

from extraction.contracts import ReceiptData
from extraction.pipeline import Pipeline
from extraction.reporting import to_dict, write_json, write_result, write_summary
from extraction.runner import new_run
from extraction.steps.validate import ValidateReceiptStep
from extraction.steps.parsing import GeminiParseStep, ParseReceiptStep
from extraction.steps.parsing.gemini import MAX_OUTPUT_TOKENS, PROMPT, build_request

from .parsing import _restore_ocr, _summary, evaluate_receipt

WORKER_DIR = Path(__file__).resolve().parents[1]
# USD per million tokens, standard paid tier checked 2026-10-19.
PRICES = {"gemini-2.5-flash-lite": (Decimal("0.10"), Decimal("0.40")),
          "gemini-3.1-flash-lite": (Decimal("0.25"), Decimal("1.50"))}


def load_api_key(env_file=WORKER_DIR / ".env"):
    """Read just this key; never interpolate, execute or log environment-file values."""
    key = os.environ.get("GEMINI_API_KEY", "").strip()
    if not key and env_file.is_file():
        for line in env_file.read_text(encoding="utf-8-sig").splitlines():
            name, separator, value = line.strip().partition("=")
            if separator and name.strip() == "GEMINI_API_KEY":
                key = value.strip()
                if len(key) >= 2 and key[0] == key[-1] and key[0] in "\"'":
                    key = key[1:-1]
                break
    if not key or key == "YOUR_GEMINI_API_KEY":
        raise ValueError("Set GEMINI_API_KEY in the environment or worker .env; never paste it into chat")
    if not re.fullmatch(r"[A-Za-z0-9_-]+", key):
        raise ValueError("GEMINI_API_KEY has an invalid format")
    return key


def request_bound(payload, model):
    """Conservative text-token reservation, not a provider billing guarantee."""
    input_bound = len(json.dumps(payload, ensure_ascii=False).encode("utf-8")) + 4096
    input_price, output_price = PRICES[model]
    return (input_bound * input_price + MAX_OUTPUT_TOKENS * output_price) / 1_000_000


def _sources(ocr_dir, limit):
    manifest = ocr_dir / "holdout-manifest.json"
    names = (json.loads(manifest.read_text(encoding="utf-8"))["new_sources"] if manifest.is_file()
             else [path.name for path in ocr_dir.iterdir() if path.is_dir() and (path / "ocr.json").is_file()])
    names = sorted(names)
    if not names or limit < 1:
        raise ValueError("No saved OCR cases found, or invalid limit")
    count = min(limit, len(names))
    selected = [names[i * (len(names) - 1) // max(1, count - 1)] for i in range(count)]
    if any(Path(name).name != name or name in (".", "..") for name in selected):
        raise ValueError("Invalid source name in OCR manifest")
    return selected


def run_gemini(ocr_dir, references_dir, output_dir, *, model="gemini-3.1-flash-lite",
               limit=10, execute=False, max_cost_usd=Decimal("1.00")):
    ocr_dir, references_dir = Path(ocr_dir), Path(references_dir)
    max_cost_usd = Decimal(str(max_cost_usd))
    if model not in PRICES or not max_cost_usd.is_finite() or max_cost_usd <= 0:
        raise ValueError("Invalid model or cost limit")
    sources, references = [], {}
    for name in _sources(ocr_dir, limit):
        path = ocr_dir / name / "ocr.json"
        raw = json.loads(path.read_text(encoding="utf-8"))
        ocr = _restore_ocr(raw)
        sources.append((name, ocr, build_request(ocr)))
        ref_path = references_dir / f"{name}.expected.json"
        if not ref_path.is_file():
            ref_path = references_dir / f"{Path(name).stem}.expected.json"
        if ref_path.is_file():
            reference = json.loads(ref_path.read_text(encoding="utf-8"))
            if reference["source"] != name:
                raise ValueError("Reference source does not match selected OCR")
            if (references_dir / name).is_dir():
                from extraction.runner import MIME_TYPES
                files = sorted(path.name for path in (references_dir / name).iterdir()
                               if path.is_file() and path.suffix.lower() in MIME_TYPES)
                if reference.get("sources") != files:
                    raise ValueError("Reference sources must match ordered input files")
            # Validate evaluation inputs before spending tokens on any case.
            evaluate_receipt(reference, ReceiptData())
            references[name] = reference
    reservation = sum((request_bound(payload, model) for _, _, payload in sources), Decimal(0))
    if execute and reservation > max_cost_usd:
        raise ValueError(f"Conservative request reservation ${reservation:.4f} exceeds budget ${max_cost_usd}")
    key = load_api_key() if execute else None
    root = new_run(output_dir)
    metadata = {"model": model, "execute": execute, "sources": [name for name, _, _ in sources],
                "input_run": str(ocr_dir.resolve()), "max_cost_usd": str(max_cost_usd),
                "conservative_reservation_usd": str(reservation),
                "prompt_sha256": hashlib.sha256(PROMPT.encode()).hexdigest(),
                "prices_usd_per_million_tokens": [str(rate) for rate in PRICES[model]],
                "prices_checked": "2026-10-19", "calls": []}
    step = GeminiParseStep(key, model, WORKER_DIR / ".cache" / "gemini") if execute else None
    pipeline = Pipeline([("gemini", step), ("validate", ValidateReceiptStep())]) if execute else None
    baseline_pipeline = Pipeline([("parse", ParseReceiptStep()), ("validate", ValidateReceiptStep())])
    results, ai_cases, baseline_cases = {}, [], []
    for name, ocr, payload in sources:
        folder = root / name
        if not execute:
            folder.mkdir()
        started = perf_counter()
        if execute:
            result = pipeline.run(ocr)
            result.captured_steps["ocr"] = ocr
            write_result(folder, result, save_images=False)
            results[name] = result
            response = step.response
            if response is not None:
                write_json(folder / "api-response.json", response)
            usage = response.get("usageMetadata", {}) if response else {}
            cost = None
            if step.cache_hit:
                cost = Decimal(0)
            elif "promptTokenCount" in usage and "candidatesTokenCount" in usage:
                cost = (usage["promptTokenCount"] * PRICES[model][0]
                        + (usage["candidatesTokenCount"] + usage.get("thoughtsTokenCount", 0)) * PRICES[model][1]) / 1_000_000
            metadata["calls"].append({"source": name, "ok": result.ok, "cache_hit": step.cache_hit,
                                      "model_version": response.get("modelVersion") if response else None,
                                      "usage": usage, "seconds": perf_counter() - started,
                                      "estimated_cost_usd": str(cost) if cost is not None else None})
            if name in references:
                reference = references[name]
                predicted = result.output if isinstance(result.output, ReceiptData) else ReceiptData()
                ai_case = evaluate_receipt(reference, predicted)
                ai_case["execution_error"] = to_dict(result.error)
                ai_cases.append(ai_case)
                baseline = baseline_pipeline.run(ocr)
                write_json(folder / "rule-result.json", to_dict(baseline))
                baseline_cases.append(evaluate_receipt(reference, baseline.output if baseline.ok else ReceiptData()))
            print(f"{name}: {'ok' if result.ok else 'error'}", flush=True)
        write_json(folder / "request.json", {"model": model, "body": payload})
        (root / "progress.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
        if step is not None and step.api_failed:
            metadata["stop_reason"] = "API/network failure; remaining cases were not sent"
            break
    metadata["unattempted_sources"] = ([name for name, _, _ in sources[len(metadata["calls"]):]] if execute else [])
    write_json(root / "gemini-run.json", metadata)
    if execute:
        write_summary(root, results)
        comparison = {"gemini": _summary(ai_cases), "rules": _summary(baseline_cases),
                      "gemini_cases": ai_cases, "rules_cases": baseline_cases,
                      "note": "Failed Gemini calls count as empty predictions. Alignment is heuristic; references are unreviewed."}
        write_json(root / "comparison.json", comparison)
        lines = ["# Gemini vs parser regułowy", "", f"Model: {model}; wybrane przypadki: {len(sources)}; próby: {len(metadata['calls'])}.",
                 "Błędy API liczone jako brak predykcji. Wzorce wymagają sprawdzenia; dopasowanie pozycji jest heurystyką.", "",
                 "| Pole | Reguły: zgodne | Gemini: zgodne |", "|---|---:|---:|"]
        for field in comparison["gemini"]["fields"]:
            lines.append(f"| {field} | {comparison['rules']['fields'].get(field, {}).get('match', 0)} | "
                         f"{comparison['gemini']['fields'][field].get('match', 0)} |")
        (root / "comparison.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{'Executed' if execute else 'Prepared, no API calls'}: {root}", flush=True)
    return root


def run_failed(root):
    metadata = json.loads((Path(root) / "gemini-run.json").read_text(encoding="utf-8"))
    return metadata["execute"] and (bool(metadata["unattempted_sources"])
                                    or any(not call["ok"] for call in metadata["calls"]))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("ocr_dir", type=Path)
    parser.add_argument("--references", type=Path, default=WORKER_DIR / "data" / "input")
    parser.add_argument("--output", type=Path, default=WORKER_DIR / "data" / "output")
    parser.add_argument("--model", choices=PRICES, default="gemini-3.1-flash-lite")
    parser.add_argument("--limit", type=int, default=10)
    parser.add_argument("--max-cost-usd", type=Decimal, default=Decimal("1.00"))
    parser.add_argument("--execute", action="store_true", help="Send selected OCR text/geometry to Google Gemini")
    args = parser.parse_args()
    try:
        root = run_gemini(args.ocr_dir, args.references, args.output, model=args.model, limit=args.limit,
                          execute=args.execute, max_cost_usd=args.max_cost_usd)
        if run_failed(root):
            parser.exit(1, "Some Gemini cases failed; see result.json and gemini-run.json\n")
    except (ValueError, OSError) as exc:
        parser.exit(1, f"{exc}\n")
