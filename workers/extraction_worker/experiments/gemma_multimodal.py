"""Frozen paired study: saved text pipeline vs images vs images with OCR."""

import argparse
import hashlib
import json
import re
from collections import Counter
from decimal import Decimal
from pathlib import Path
from time import perf_counter

from extraction.providers import OpenRouterClient
from extraction.contracts import ImageOCRInput
from extraction.reporting import to_dict, write_json
from extraction.requests import BudgetedRequests, cache_only
from extraction.runner import new_run, read_sources
from extraction.steps.analysis import analyzed_receipt
from extraction.steps.parsing import ParseReceiptStep
from extraction.analysis.receipt_images import ReceiptImageAnalyzer, build_vision_request, request_summary, vision_bound
from extraction.steps.validate import validate_receipt
from extraction.workflows import vision_analysis_pipeline, vision_ocr_analysis_pipeline
from .budget import SettledBudget
from .evaluation import normalize_name
from .gemma import WORKER_DIR, load_config
from .parsing import _restore_ocr, _summary, evaluate_receipt

FIXTURES = Path(__file__).parent / "fixtures/gemma-vision-v2"
MANIFEST = Path(__file__).parent / "fixtures/gemma-benchmark-v2/manifest.json"
METHODS = ("pipeline", "vision", "vision_ocr")


def receipt_score(reference, analysis=None):
    """Count failed documents as missing fields. All policies fixed before calls."""
    prediction = to_dict(analysis.receipt) if analysis else {}
    annotations = to_dict(analysis.annotations) if analysis else []
    for item, annotation in zip(prediction.get("items", []), annotations):
        item["name"] = annotation["name_corrected"]
    evaluation = evaluate_receipt(reference, prediction)
    scores = Counter(documents=1, completed=int(analysis is not None), categories_expected=len(reference["items"]),
                     names_expected=len(reference["items"]))
    for entry in evaluation["groups"]["items"]["entries"]:
        expected = reference["items"][entry["expected_index"]]
        index = entry["observed_index"]
        if index is None:
            continue
        actual = annotations[index]
        scores["categories_primary_correct"] += actual["category"] == expected["category"]
        scores["categories_accepted_correct"] += actual["category"] in expected.get("accepted_categories", [expected["category"]])
        def name(text):
            marker = expected.get("tax_code")
            if marker and isinstance(marker, str):
                text = re.sub(r"(?:\s+|-)" + re.escape(marker) + r"$", "", text or "", flags=re.I)
            return normalize_name(text or "")
        scores["names_vat_normalized_correct"] += bool(expected["name"]) and name(expected["name"]) == name(actual["name_corrected"])
        # A missing, unprinted zero discount is not a misread nonzero discount.
        check = entry["fields"]["discount"]
        if check["expected"] == "0.00" and check["observed"] is None:
            scores["absent_zero_discount"] += 1
    for group in ("items", "deposits", "deposit_returns"):
        details = evaluation["groups"][group]
        scores[f"{group}_missing"] += len(details["missing_indices"])
        scores[f"{group}_excess"] += len(details["excess_indices"])
    return {"evaluation": evaluation, "counts": dict(scores)}


def aggregate(cases, method):
    records = [c[method] for c in cases]
    counts = Counter()
    for record in records:
        counts.update(record["score"]["counts"])
    return {"counts": dict(counts), "fields": _summary([r["score"]["evaluation"] for r in records]),
            "validation_warnings": dict(Counter(w["code"] for r in records for w in r.get("validation", [])))}


def audit_study(report):
    """Supplementary post-hoc audit; does not replace the frozen primary scores.

    A reference null is not proof of absence: list those fills for visual review,
    rather than automatically calling each one a hallucination. Missing values,
    wrong known numbers, and mathematically consistent errors remain distinct.
    """
    result = {"status": "posthoc_supplement_not_preregistered", "methods": {}}
    arithmetic_codes = {"invalid_number", "item_price_mismatch", "item_discount_mismatch",
                        "total_mismatch", "amount_due_mismatch", "deposit_sign_mismatch"}
    for method in report.get("methods", METHODS):
        audit = {"different_known_numbers": [], "missing_known_numbers": [], "reference_null_fills": [],
                 "unflagged_number_error_documents": [], "core_financial_exact_documents": [], "classification_errors": []}
        for case in report["cases"]:
            record = case[method]
            if record["status"] != "completed":
                continue
            evaluation, reference = record["score"]["evaluation"], case["reference"]
            document_errors = False
            core_exact = all(check["status"] in ("match", "not_annotated") for field, check in evaluation["document_fields"].items()
                             if field in ("total", "amount_due"))
            for field in ("total", "amount_due"):
                check = evaluation["document_fields"][field]
                value = {"source": case["source"], "field": field, "expected": check["expected"], "observed": check["observed"]}
                if check["status"] == "different":
                    audit["different_known_numbers"].append(value)
                    document_errors = True
                elif check["status"] == "missing":
                    audit["missing_known_numbers"].append(value)
            for group in ("items", "deposits", "deposit_returns"):
                details = evaluation["groups"][group]
                core_exact &= not details["missing_indices"] and not details["excess_indices"]
                for entry in details["entries"]:
                    path = f"{group}[{entry['expected_index']}]"
                    for field, check in entry["fields"].items():
                        if field == "name":
                            continue
                        value = {"source": case["source"], "field": f"{path}.{field}",
                                 "expected": check["expected"], "observed": check["observed"]}
                        if check["status"] == "different":
                            audit["different_known_numbers"].append(value)
                            document_errors = True
                        elif check["status"] == "missing":
                            audit["missing_known_numbers"].append(value)
                        elif check["expected"] is None and check["observed"] is not None:
                            audit["reference_null_fills"].append(value)
                        if field in ("quantity", "unit_price", "total"):
                            core_exact &= check["status"] in ("match", "not_annotated")
                    if group == "items" and entry["observed_index"] is not None:
                        expected = reference["items"][entry["expected_index"]]
                        observed = record["analysis"]["annotations"][entry["observed_index"]]
                        if observed["category"] not in expected.get("accepted_categories", [expected["category"]]):
                            audit["classification_errors"].append({"source": case["source"], "name": expected["name"],
                                "expected": expected["category"], "observed": observed["category"], "needs_review": observed["needs_review"]})
            if document_errors and not any(w["code"] in arithmetic_codes for w in record.get("validation", [])):
                audit["unflagged_number_error_documents"].append(case["source"])
            if core_exact:
                audit["core_financial_exact_documents"].append(case["source"])
        audit["response_cost_usd"] = None if method == "pipeline" else str(sum((Decimal(str((c[method].get("usage") or {}).get("cost", 0))) for c in report["cases"]), Decimal(0)))
        audit["new_cost_usd"] = str(sum((Decimal(str(c[method].get("new_cost_usd",
            0 if c[method].get("cache_hit", False) else (c[method].get("usage") or {}).get("cost", 0))))
            for c in report["cases"]), Decimal(0)))
        result["methods"][method] = audit
    return result


def markdown(report):
    methods = report.get("methods", METHODS)
    labels = {"pipeline": "OCR → parser → Gemma", "vision": "Zdjęcie", "vision_ocr": "Zdjęcie + OCR"}
    lines = ["# " + report.get("title", "Większy test: zdjęcie i zdjęcie + OCR"), "",
             "Próba celowa, referencje asystenta sprawdzone na zdjęciach przed predykcjami; bez zatwierdzenia człowieka.", "",
             "| Miara | " + " | ".join(labels[m] for m in methods) + " |", "|---|" + "---:|" * len(methods)]
    for label, key, denominator in [
        ("Ukończone dokumenty", "completed", "documents"),
        ("Kategorie podstawowe", "categories_primary_correct", "categories_expected"),
        ("Kategorie z wcześniej dopuszczonymi alternatywami", "categories_accepted_correct", "categories_expected"),
        ("Nazwy po usunięciu wydrukowanego znacznika VAT", "names_vat_normalized_correct", "names_expected")]:
        values = [report["summary"][m]["counts"] for m in methods]
        lines.append(f"| {label} | " + " | ".join(f"{v.get(key, 0)}/{v[denominator]}" for v in values) + " |")
    for label, key in [("Brakujące zakupy", "items_missing"), ("Nadmiarowe zakupy", "items_excess")]:
        lines.append(f"| {label} | " + " | ".join(str(report["summary"][m]["counts"].get(key, 0)) for m in methods) + " |")
    for field in ("date", "total", "amount_due", "items.quantity", "items.unit_price", "items.before_discount", "items.discount", "items.total", "deposits.total", "deposit_returns.total"):
        cells = []
        for method in methods:
            counts = report["summary"][method]["fields"]["fields"].get(field, {})
            denominator = sum(v for k, v in counts.items() if k != "not_annotated")
            cells.append(f"{counts.get('match', 0)}/{denominator}" if denominator else "—")
        lines.append(f"| {field} | " + " | ".join(cells) + " |")
    lines.extend(["", "## Nowe dokumenty i kontrola powtórzonych", ""])
    for group, summaries in report["cohorts"].items():
        lines.append(f"### {group}")
        for method in methods:
            c = summaries[method]["counts"]
            lines.append(f"- {method}: dokumenty {c.get('completed', 0)}/{c.get('documents', 0)}, "
                         f"kategorie {c.get('categories_primary_correct', 0)}/{c.get('categories_expected', 0)}, "
                         f"braki {c.get('items_missing', 0)}, nadmiar {c.get('items_excess', 0)}.")
        lines.append("")
    lines.extend(["## Koszt i ograniczenia", "",
        f"Nowe wywołania {report['api_calls']}, koszt API {report['reported_cost_usd']} USD, limit {report['budget_usd']} USD.",
        "Rezerwacja pełnego kontekstu następuje przed każdym wywołaniem. Znany koszt odpowiedzi zwalnia "
        "niewykorzystaną część rezerwacji; timeout i brak kosztu zachowują pełną rezerwację. Wszystkie próby pozostają w ledgerze.",
        "Oryginalne obrazy i OCR bez nazw plików są danymi wejściowymi. Referencje, kategorie wzorcowe i parser nie trafiają do modelu.",
        "Zapisany tekstowy pipeline korzystał z CoreWeave, obrazy preferują DeepInfra; różnica dostawcy i stary/nowy czas pomiaru ograniczają wnioskowanie.",
        "Nazwy mają kryterium transkrypcji, nie pełną ocenę semantyczną. Niezerowe rabaty i wszystkie kwoty pozostają bez automatycznych poprawek.",
        "Brakujące/odrzucone odpowiedzi są liczone jako braki, nie usuwane z mianownika. Dopasowanie pozycji jest heurystyczne; nadmiar raportowany osobno.",
        "OCR jest zachowany z poprzedniego lokalnego przebiegu; koszt i czas jego ponownego wykonania nie są mierzone. Promptów nie zmieniano po obejrzeniu wyników.", ""])
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--execute", action="store_true")
    mode.add_argument("--replay-cache", action="store_true")
    parser.add_argument("--baseline-run", type=Path, required=True)
    parser.add_argument("--limit", type=int, choices=range(1, 25), default=24)
    parser.add_argument("--budget", default="0.45")
    args = parser.parse_args(argv)
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    baseline = {}
    for split in ("development", "holdout"):
        baseline.update({c["source"]: c for c in json.loads((args.baseline_run / f"{split}-baseline/report.json").read_text(encoding="utf-8"))["cases"]})
    root = new_run(WORKER_DIR / "data/output")
    budget = SettledBudget(root, args.budget, 2 * args.limit)
    previous = {json.loads(p.read_text(encoding="utf-8"))["source"] for p in (FIXTURES.parent / "gemma-vision-v1").glob("*.expected.json")}
    prepared, plan = [], []
    for entry in manifest["cases"][:args.limit]:
        source = entry["source"]
        ref_path = FIXTURES / entry["split"] / f"{Path(source).stem}.expected.json"
        ref = json.loads(ref_path.read_text(encoding="utf-8"))
        if ref["source"] != source or any("category" not in i for i in ref["items"]) or ref["review_status"] != "assistant_verified_not_human":
            raise ValueError("Unverified/stale study references")
        ocr = _restore_ocr(json.loads((WORKER_DIR / entry["ocr_path"]).read_text(encoding="utf-8")))
        receipt = ParseReceiptStep().run(ocr).output
        if to_dict(receipt) != entry["parser"] or baseline[source]["parser"] != entry["parser"]:
            raise ValueError("Frozen parser snapshot changed")
        images = read_sources(tuple(WORKER_DIR / p for p in entry["image_paths"]))
        folder = root / source
        folder.mkdir()
        for method in METHODS[1:]:
            payload = build_vision_request(images.files, ocr if method == "vision_ocr" else None)
            write_json(folder / f"{method}-request-summary.json", request_summary(payload))
            plan.append({"source": source, "method": method, "request_sha256": hashlib.sha256(json.dumps(
                payload, ensure_ascii=False, sort_keys=True).encode()).hexdigest(), "reference_sha256": hashlib.sha256(ref_path.read_bytes()).hexdigest()})
        prepared.append((entry, ref, ocr, images, receipt))
    write_json(root / "plan.json", {"requests": plan, "budget_usd": str(budget.limit), "max_calls": budget.max_calls,
        "baseline_run": str(args.baseline_run.resolve()), "methods": METHODS, "stop": "no retries; quota/config error or 3 consecutive failures",
        "normalization": "exact numbers; names strip only reference tax code suffix; null zero discounts reported separately",
        "actual_cost_estimate_not_guarantee_usd": "0.06", "per_attempt_full_context_bound": str(vision_bound())})
    print(f"STUDY: {root}; {len(prepared)} documents; max {budget.max_calls} calls; budget {budget.limit} USD", flush=True)
    if not args.execute and not args.replay_cache:
        return 0
    key = "offline-cache-only" if args.replay_cache else load_config()["api_key"]
    report = {"cases": []}
    failures, halted = 0, False
    for entry, ref, ocr, images, receipt in prepared:
        source = entry["source"]
        folder = root / source
        base = analyzed_receipt(receipt, baseline[source]["enrichment"])
        case = {"source": source, "split": entry["split"], "cohort": "previous_vision_5" if source in previous else "new_vision_19",
                "reference": ref, "pipeline": {"status": "completed", "analysis": to_dict(base), "score": receipt_score(ref, base),
                    "validation": to_dict(validate_receipt(receipt))}}
        for method in METHODS[1:]:
            record = {"status": "skipped_after_error", "score": receipt_score(ref)}
            if not halted:
                policy = cache_only if args.replay_cache else BudgetedRequests(budget, lambda body: vision_bound())
                analyzer = ReceiptImageAnalyzer(
                    OpenRouterClient(key),
                    allow_paid=True,
                    cache_dir=WORKER_DIR / ".cache/gemma-vision",
                    request_policy=policy,
                )
                workflow = vision_ocr_analysis_pipeline(analyzer) if method == "vision_ocr" else vision_analysis_pipeline(analyzer)
                value = ImageOCRInput(images, ocr) if method == "vision_ocr" else images
                started = perf_counter()
                result = workflow.run(value)
                record.update(seconds=round(perf_counter()-started, 3), cache_hit=analyzer.cache_hit,
                              steps=to_dict(result.steps), warnings=to_dict(result.warnings))
                if analyzer.response is not None:
                    write_json(folder / f"{method}-response.json", analyzer.response)
                    record.update(provider=analyzer.response.get("provider"), usage=analyzer.response.get("usage"))
                if result.ok:
                    failures = 0
                    record.update(status="completed", analysis=to_dict(result.output), score=receipt_score(ref, result.output),
                                  validation=to_dict(validate_receipt(result.output.receipt)))
                else:
                    failures += 1
                    record.update(status="failed", error=to_dict(result.error))
                    message = result.error.message
                    halted = failures >= 3 or "budget/call cap" in message or "exceeded reservation" in message or any(f"HTTP {status}" in message for status in (400,401,402,403,404,429))
                print(f"{source} {method}: {record['status']}; calls={budget.calls}; cost={budget.cost}", flush=True)
            case[method] = record
            write_json(folder / f"{method}-result.json", record)
        report["cases"].append(case)
        (root / "progress.json").write_text(json.dumps(to_dict(report), ensure_ascii=False, indent=2), encoding="utf-8")
    report.update(summary={m: aggregate(report["cases"], m) for m in METHODS},
                  cohorts={group: {m: aggregate([c for c in report["cases"] if c["cohort"] == group], m) for m in METHODS}
                           for group in sorted({c["cohort"] for c in report["cases"]})},
                  api_calls=budget.calls, reported_cost_usd=str(budget.cost), budget_usd=str(budget.limit))
    write_json(root / "comparison.json", report)
    (root / "comparison.md").write_text(markdown(report), encoding="utf-8")
    write_json(root / "posthoc-audit.json", audit_study(report))
    print(f"COMPLETE: {root}; {budget.calls} calls; {budget.cost} USD", flush=True)
    return int(halted)


if __name__ == "__main__":
    raise SystemExit(main())
