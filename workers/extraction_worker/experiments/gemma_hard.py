"""Hard natural receipts and an explicitly separated incomplete-document control."""

import argparse
import hashlib
import json
import re
from dataclasses import replace
from pathlib import Path
from time import perf_counter

from extraction.contracts import ImageOCRInput, PreparedDocument
from extraction.reporting import to_dict, write_json
from extraction.requests import BudgetedRequests, cache_only
from extraction.runner import new_run, read_sources
from extraction.steps.enrich import PaidOpenRouterEnricher
from extraction.steps.parsing import ParseReceiptStep
from extraction.steps.parsing.vision import GemmaVisionParser, build_vision_request, request_summary, vision_bound
from extraction.steps.validate import validate_receipt
from extraction.workflows import text_analysis_pipeline, vision_ocr_analysis_pipeline
from .budget import SettledBudget
from .gemma import WORKER_DIR, load_config
from .gemma_multimodal import aggregate, audit_study, markdown, receipt_score
from .parsing import _restore_ocr

FIXTURES = Path(__file__).parent / "fixtures/gemma-hard-v1"
METHODS = ("pipeline", "vision_ocr")


def single_page_ocr(ocr, page_number, sources):
    """Never leak the other capture's OCR into a deliberately incomplete input."""
    pages = [p for p in ocr.pages if p.page_number == page_number]
    images = [p for p in ocr.document.pages if p.number == page_number]
    if len(pages) != 1 or len(images) != 1 or len(sources.files) != 1:
        raise ValueError("Partial control requires exactly one matching OCR/image page")
    if Path(pages[0].source_name).name != sources.files[0].name:
        raise ValueError("Partial control OCR belongs to another source")
    page = replace(pages[0], page_number=1, source_index=1, source_page_number=1)
    image = replace(images[0], number=1, source_index=1, source_page_number=1)
    return replace(ocr, pages=(page,), document=PreparedDocument(sources.files, (image,)))


def partial_checks(reference, analysis):
    receipt = to_dict(analysis.receipt)
    alignment = receipt_score(reference, analysis)["evaluation"]["groups"]["items"]["entries"]
    checks = []
    for field in reference.get("expected_abstentions", []):
        match = re.fullmatch(r"items\[(\d+)\]\.(\w+)", field)
        if match:
            index = int(match[1])
            # The clipped item may be omitted; do not borrow its neighbor's values.
            observed_index = alignment[index]["observed_index"]
            if observed_index is None:
                checks.append({"field": field, "status": "requires_item_alignment_review"})
                continue
            value = receipt["items"][observed_index][match[2]]
        elif field not in receipt:
            checks.append({"field": field, "status": "not_in_output_contract"})
            continue
        else:
            value = receipt[field]
        if field in ("deposits", "deposit_returns"):
            status = "unknown_not_distinguished_from_empty_group" if not value else "group_claim_requires_visual_review"
        else:
            status = "abstained" if value is None else "filled_unseen_field"
        checks.append({"field": field, "status": status, "observed": value})
    return checks


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--execute", action="store_true")
    mode.add_argument("--replay-cache", action="store_true")
    parser.add_argument("--ocr-dir", type=Path, required=True)
    parser.add_argument("--fixtures", type=Path, default=FIXTURES)
    parser.add_argument("--budget", default="0.35")
    args = parser.parse_args(argv)
    manifest = json.loads((args.fixtures / "manifest.json").read_text(encoding="utf-8"))
    prepared = []
    for entry in manifest["cases"]:
        ref_path = args.fixtures / f"{Path(entry['source']).stem}.expected.json"
        reference = json.loads(ref_path.read_text(encoding="utf-8"))
        if reference["source"] != entry["source"] or reference["review_status"] != "assistant_verified_not_human":
            raise ValueError("Reference is not verified or belongs to a different source")
        if any("category" not in item for item in reference["items"]):
            raise ValueError("Missing frozen item categories")
        sources = read_sources(tuple(WORKER_DIR / p for p in entry["image_paths"]))
        raw_ocr = json.loads((args.ocr_dir / entry["ocr_source"] / "ocr.json").read_text(encoding="utf-8"))
        ocr = _restore_ocr(raw_ocr)
        if entry.get("ocr_page"):
            ocr = single_page_ocr(ocr, entry["ocr_page"], sources)
        payload = build_vision_request(sources.files, ocr)
        parser_snapshot = to_dict(ParseReceiptStep().run(ocr).output)
        prepared.append((entry, reference, sources, ocr, payload, hashlib.sha256(ref_path.read_bytes()).hexdigest(), parser_snapshot))
    root = new_run(WORKER_DIR / "data/output")
    budget = SettledBudget(root, args.budget, 60)
    write_json(root / "plan.json", {"dataset": manifest, "methods": METHODS, "budget_usd": str(budget.limit),
        "manifest_sha256": hashlib.sha256((args.fixtures / "manifest.json").read_bytes()).hexdigest(),
        "cases": [{"source": x[0]["source"], "reference_sha256": x[5], "vision_request_sha256": hashlib.sha256(
            json.dumps(x[4], ensure_ascii=False, sort_keys=True).encode()).hexdigest(),
            "parser_snapshot_sha256": hashlib.sha256(json.dumps(x[6], ensure_ascii=False, sort_keys=True).encode()).hexdigest()} for x in prepared],
        "planned_calls_before_cache": sum((len(x[6]["items"])+7)//8 + 1 for x in prepared),
        "policy": "Same prompts, no retries, stop on config/quota/budget error or three consecutive failures. Primary scores exclude related capture and partial controls."})
    for entry, _, _, _, payload, _, snapshot in prepared:
        folder = root / entry["source"]
        folder.mkdir()
        write_json(folder / "vision-request-summary.json", request_summary(payload))
        write_json(folder / "parser-snapshot.json", snapshot)
    print(f"HARD STUDY: {root}; {len(prepared)} presentations; budget={budget.limit} USD", flush=True)
    if not args.execute and not args.replay_cache:
        return 0
    key = "offline-cache-only" if args.replay_cache else load_config()["api_key"]
    report = {"title": "Trudne naturalne paragony: tekstowy pipeline a zdjęcie + OCR", "methods": METHODS, "cases": []}
    halted, failures = False, 0
    for entry, reference, sources, ocr, _, _, _ in prepared:
        case = {**entry, "reference": reference}
        folder = root / entry["source"]
        for method in METHODS:
            record = {"status": "skipped_after_error", "score": receipt_score(reference)}
            if not halted:
                if method == "pipeline":
                    bound = lambda body: PaidOpenRouterEnricher.request_bound(json.loads(body))
                    policy = cache_only if args.replay_cache else BudgetedRequests(budget, bound)
                    def capture(body, send):
                        response = policy(body, send)
                        write_json(folder / f"text-response-attempt-{budget.calls}.json", response)
                        return response
                    client = PaidOpenRouterEnricher(key, max_output_tokens=2048,
                        cache_dir=WORKER_DIR / ".cache/gemma-hard-text", request_policy=capture)
                    workflow, value = text_analysis_pipeline(client), ocr
                else:
                    policy = cache_only if args.replay_cache else BudgetedRequests(budget, lambda body: vision_bound())
                    client = GemmaVisionParser(key, cache_dir=WORKER_DIR / ".cache/gemma-vision", request_policy=policy)
                    workflow, value = vision_ocr_analysis_pipeline(client), ImageOCRInput(sources, ocr)
                start_cost, start_calls = budget.cost, budget.calls
                started = perf_counter()
                outcome = workflow.run(value)
                record.update(seconds=round(perf_counter()-started, 3), steps=to_dict(outcome.steps),
                              warnings=to_dict(outcome.warnings), new_calls=budget.calls-start_calls,
                              new_cost_usd=str(budget.cost-start_cost), cache_hit=budget.calls == start_calls)
                if method == "vision_ocr" and client.response is not None:
                    write_json(folder / "vision-response.json", client.response)
                    record.update(provider=client.response.get("provider"), usage=client.response.get("usage"))
                if outcome.ok:
                    failures = 0
                    record.update(status="completed", analysis=to_dict(outcome.output), score=receipt_score(reference, outcome.output),
                                  validation=to_dict(validate_receipt(outcome.output.receipt)))
                    if entry["cohort"] == "partial_document_control":
                        record["abstention_checks"] = partial_checks(reference, outcome.output)
                else:
                    failures += 1
                    record.update(status="failed", error=to_dict(outcome.error))
                    msg = outcome.error.message
                    halted = failures >= 3 or "budget/call cap" in msg or "exceeded reservation" in msg or any(
                        f"HTTP {s}" in msg for s in (400, 401, 402, 403, 404, 429))
                print(f"{entry['source']} {method}: {record['status']}; calls={budget.calls}; cost={budget.cost}", flush=True)
            case[method] = record
            write_json(folder / f"{method}-result.json", record)
        report["cases"].append(case)
        (root / "progress.json").write_text(json.dumps(to_dict(report), ensure_ascii=False, indent=2), encoding="utf-8")
    primary = [c for c in report["cases"] if c["cohort"] == "full_primary_transactions"]
    report.update(summary={m: aggregate(primary, m) for m in METHODS},
                  cohorts={g: {m: aggregate([c for c in report["cases"] if c["cohort"] == g], m) for m in METHODS}
                           for g in sorted({c["cohort"] for c in report["cases"]})},
                  api_calls=budget.calls, reported_cost_usd=str(budget.cost), budget_usd=str(budget.limit))
    write_json(root / "comparison.json", report)
    text = markdown(report).replace("Zapisany tekstowy pipeline korzystał z CoreWeave, obrazy preferują DeepInfra; różnica dostawcy i stary/nowy czas pomiaru ograniczają wnioskowanie.",
        "Obie ścieżki wykonano w tym badaniu. Główne metryki obejmują tylko niezależne pełne transakcje; kontrole ujęcia i urwanego dokumentu są oddzielne.")
    (root / "comparison.md").write_text(text, encoding="utf-8")
    write_json(root / "posthoc-audit.json", audit_study(report))
    print(f"COMPLETE: {root}; calls={budget.calls}; cost={budget.cost} USD", flush=True)
    return int(halted)


if __name__ == "__main__":
    raise SystemExit(main())
