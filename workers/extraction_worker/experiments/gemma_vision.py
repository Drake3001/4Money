"""Paired image-only vs saved OCR/rules/Gemma comparison with five verified receipts."""

import argparse
import copy
import hashlib
import json
from decimal import Decimal
from pathlib import Path
from time import perf_counter

from extraction.contracts import SourceFile, SourceFiles
from extraction.requests import BudgetedRequests, cache_only
from extraction.workflows import vision_analysis_pipeline
from extraction.reporting import to_dict, write_json
from extraction.runner import new_run
from extraction.steps.parsing.vision import GemmaVisionParser, build_vision_request, request_summary, vision_bound
from extraction.steps.validate import validate_receipt
from .gemma import WORKER_DIR, load_config
from .budget import Budget
from .parsing import _summary, evaluate_receipt

SOURCES = ("Screenshot_20261011_140209_appka.jpg", "IMG_6796.JPEG", "20261011_130153.jpg",
           "20261016_210327.jpg", "lidl-20261017-524861-parts")
FIXTURES = Path(__file__).parent / "fixtures/gemma-vision-v1"


def expected_categories(source, count):
    if source == "IMG_6796.JPEG":
        return ["żywność i napoje", "dom i ogród"]
    if source == "20261011_130153.jpg":
        return ["alkohol i tytoń"]
    if source == "20261016_210327.jpg":
        return ["restauracje i kawiarnie", "restauracje i kawiarnie", "alkohol i tytoń"]
    if source == "lidl-20261017-524861-parts":
        return ["dom i ogród" if i in (2, 5) else "żywność i napoje" for i in range(count)]
    return ["żywność i napoje"] * count


def category_score(evaluation, metadata, categories):
    entries = evaluation["groups"]["items"]["entries"]
    correct = 0
    for entry in entries:
        observed = entry["observed_index"]
        if observed is not None and observed < len(metadata):
            correct += metadata[observed]["category"] == categories[entry["expected_index"]]
    return {"correct": correct, "expected": len(categories)}


def comparison_markdown(report):
    lines = ["# Gemma z samych zdjęć a OCR → parser → Gemma", "",
             "Mała, celowo dobrana próba. Te same dokumenty i wspólne referencje sprawdzone przez asystenta "
             "na zdjęciach przed predykcjami; brak niezależnego zatwierdzenia przez człowieka.", "",
             "Vision dostała wyłącznie oryginalne bajty JPEG/PNG i instrukcję, bez OCR, nazw plików, "
             "wyników parsera, referencji ani wcześniejszych predykcji. Brak kadrowania, zmiany rozmiaru i obrotu. "
             "Kilka zdjęć jednego paragonu wysłano w jednym żądaniu.", "",
             "| Dokument | Pipeline: pozycje / braki / nadmiar | Vision: pozycje / braki / nadmiar | Vision: status / dostawca |",
             "|---|---|---|---|"]
    for case in report["cases"]:
        base = case["pipeline_evaluation"]["groups"]["items"]
        vision = case.get("vision_evaluation", {}).get("groups", {}).get("items")
        def counts(group):
            return f"{group['observed_count']} / {len(group['missing_indices'])} / {len(group['excess_indices'])}" if group else "brak odpowiedzi"
        lines.append(f"| {case['source']} | {counts(base)} | {counts(vision)} | {case['status']} / {case.get('provider', '—')} |")
    lines.extend(["", "## Pola ocenione według tego samego dopasowania pozycji", "",
                  "| Pole | Pipeline | Vision |", "|---|---|---|"])
    for field in ("store", "date", "currency", "total", "amount_due", "items.name", "items.quantity", "items.unit_price", "items.before_discount", "items.discount", "items.total", "deposits.total"):
        values = []
        for method in ("pipeline_summary", "vision_summary"):
            counts = report[method]["fields"].get(field, {})
            denominator = sum(v for k, v in counts.items() if k != "not_annotated")
            values.append(f"{counts.get('match', 0)}/{denominator}" if denominator else "—")
        lines.append(f"| {field} | {' | '.join(values)} |")
    lines.extend(["", f"Kategorie: pipeline {report['category_totals']['pipeline_correct']}/{report['category_totals']['expected']}; "
                  f"vision {report['category_totals']['vision_correct']}/{report['category_totals']['expected']}.", "",
                  f"Nowe wywołania: {report['api_calls']}; koszt zgłoszony przez API: **{report['reported_cost_usd']} USD**; "
                  f"konserwatywna rezerwacja: {report['reserved_usd']} USD.", "",
                  "Liczby pól podsumowania vision są warunkowe na ukończonych odpowiedziach. "
                  f"Ukończono {sum(c['status']=='completed' for c in report['cases'])}/{len(report['cases'])} dokumentów. "
                  "Nazwy oceniono po normalizacji znaków interpunkcyjnych, wielkości liter i odstępów; "
                  "polskie litery pozostają istotne. Dopasowanie produktów jest heurystyczne — nie jest niezależnym dowodem poprawności.", "",
                  "Czas vision obejmuje wysłanie obrazu i generowanie. Zachowany czas pipeline'u pochodzi ze "
                  "wzbogacania tekstu, nie obejmuje całego OCR/CPU. Porównanie kosztu dotyczy tylko kosztu API. "
                  "Zapisywany jest faktyczny dostawca: inny endpoint/kwantyzacja mogą wpływać na wynik, więc "
                  "nie przypisujemy całej różnicy wyłącznie zastąpieniu OCR obrazem.", "",
                  "Pochodzenie image_evidence deklaruje model. Spójność rachunkowa i wskazanie obrazu nie "
                  "dowodzą poprawnego odczytu. Unknown pozostaje null, walidacja nie przepisuje kwot."])
    if "pipeline_reported_cost_usd" in report:
        lines.extend(["", f"Historyczny koszt API tekstowej Gemmy dla tych samych dokumentów: "
                      f"{report['pipeline_reported_cost_usd']} USD. Koszt API odpowiedzi vision "
                      f"(włącznie z odczytanymi z cache): {report['vision_responses_cost_usd']} USD. "
                      "Cennik/cache/dostawca i zakres zadań mogą się różnić; to obserwowany koszt tej próby."])
    if report.get("mode") == "offline_cache_replay":
        lines.extend(["", "Ten raport powstał z cache bez dostępu do sieci. Podane seconds mierzą lokalny replay, "
                      "nie opóźnienie API. Pierwotne czasy i błędy należy odczytać z pierwotnego płatnego uruchomienia."])
    return "\n".join(lines) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--execute", action="store_true")
    mode.add_argument("--replay-cache", action="store_true", help="Re-evaluate saved responses; network calls forbidden")
    parser.add_argument("--limit", type=int, choices=range(1, 6), default=5)
    parser.add_argument("--baseline-run", type=Path, required=True,
                        help="Saved benchmark root containing development/holdout-baseline/report.json")
    parser.add_argument("--manifest", type=Path, default=Path(__file__).parent / "fixtures/gemma-benchmark-v2/manifest.json")
    parser.add_argument("--references", type=Path, default=FIXTURES)
    parser.add_argument("--output-dir", type=Path, default=WORKER_DIR / "data/output")
    parser.add_argument("--cache-dir", type=Path, default=WORKER_DIR / ".cache/gemma-vision")
    args = parser.parse_args(argv)
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    snapshots = {c["source"]: c for c in manifest["cases"]}
    baseline = {}
    for split in ("development", "holdout"):
        saved = json.loads((args.baseline_run / f"{split}-baseline/report.json").read_text(encoding="utf-8"))
        baseline.update({c["source"]: c for c in saved["cases"]})
    prepared = []
    for source in SOURCES[:args.limit]:
        reference = json.loads((args.references / f"{Path(source).stem}.expected.json").read_text(encoding="utf-8"))
        if reference["source"] != source or baseline[source]["status"] != "completed":
            raise ValueError("Reference or saved baseline mismatch")
        images = []
        for index, relative in enumerate(snapshots[source]["image_paths"], 1):
            path = WORKER_DIR / relative
            content = path.read_bytes()
            mime = "image/png" if path.suffix.casefold() == ".png" else "image/jpeg"
            images.append(SourceFile(content, mime, f"image-{index}"))
        payload = build_vision_request(images)
        prepared.append((source, reference, images, payload))
    root = new_run(args.output_dir)
    budget = Budget(root, "0.45", args.limit)
    preflight = vision_bound() * len(prepared)
    if preflight > budget.limit:
        raise ValueError("Vision plan exceeds budget before any calls")
    write_json(root / "plan.json", {"sources": [c[0] for c in prepared], "max_calls": args.limit,
               "budget_usd": "0.45", "baseline_run": str(args.baseline_run.resolve()), "preflight_bound_usd": str(preflight), "input": "original images only",
               "references": [{"source": c[0], "sha256": hashlib.sha256((args.references / f'{Path(c[0]).stem}.expected.json').read_bytes()).hexdigest()} for c in prepared]})
    for source, _, _, payload in prepared:
        folder = root / source
        folder.mkdir()
        write_json(folder / "request-summary.json", request_summary(payload))
    print(f"VISION RUN: {root}; full-context reservation {preflight} USD", flush=True)
    if not args.execute and not args.replay_cache:
        return 0
    config = {"api_key": "offline-cache-only"} if args.replay_cache else load_config()
    report = {"cases": [], "api_calls": 0, "mode": "offline_cache_replay" if args.replay_cache else "paid_execution"}
    halted = False
    for source, reference, images, payload in prepared:
        folder = root / source
        base = baseline[source]
        pipeline = copy.deepcopy(base["parser"])
        for i, metadata in enumerate(base["enrichment"]["items"]):
            pipeline["items"][i]["name"] = metadata["name_corrected"]
        categories = expected_categories(source, len(reference["items"]))
        case = {"source": source, "status": "skipped_after_error" if halted else "prepared",
                "reference_review_status": reference.get("review_status"), "reference": reference,
                "pipeline": pipeline, "pipeline_provider": base.get("provider"),
                "pipeline_usage": [c.get("usage") for c in base["chunks"]],
                "pipeline_evaluation": evaluate_receipt(reference, pipeline)}
        case["pipeline_category_score"] = category_score(case["pipeline_evaluation"], base["enrichment"]["items"], categories)
        if not halted:
            policy = cache_only if args.replay_cache else BudgetedRequests(budget, lambda body: vision_bound())
            client = GemmaVisionParser(config["api_key"], cache_dir=args.cache_dir, request_policy=policy)
            workflow = vision_analysis_pipeline(client)
            started = perf_counter()
            try:
                result = workflow.run(SourceFiles(tuple(images)))
                if not result.ok:
                    raise RuntimeError(f"{result.error.step}: {result.error.message}")
                analyzed = result.output
                enrichment = {"items": to_dict(analyzed.annotations)}
                case.update(status="completed", vision=to_dict(analyzed.receipt), enrichment=enrichment,
                            analysis=to_dict(analyzed), steps=to_dict(result.steps),
                            image_evidence=to_dict(analyzed.image_evidence), warnings=to_dict(result.warnings),
                            validation=to_dict(validate_receipt(analyzed.receipt)))
                case["vision_evaluation"] = evaluate_receipt(reference, analyzed.receipt)
                case["vision_category_score"] = category_score(case["vision_evaluation"], enrichment["items"], categories)
            except (ValueError, RuntimeError, OSError) as exc:
                case.update(status="failed", error=str(exc))
                halted = True  # No retries and no spending remaining calls on a broken route/schema.
            if client.response is not None:
                write_json(folder / "response.json", client.response)
                case.update(provider=client.response.get("provider"), usage=client.response.get("usage"))
            case.update(seconds=round(perf_counter()-started, 3), cache_hit=client.cache_hit)
        write_json(folder / "result.json", case)
        report["cases"].append(case)
        print(f"{source}: {case['status']}; provider={case.get('provider')}; calls={budget.calls}", flush=True)
    report.update(api_calls=budget.calls, reported_cost_usd=str(budget.cost), reserved_usd=str(budget.reserved),
                  pipeline_reported_cost_usd=str(sum((Decimal(str(u['cost'])) for c in report['cases']
                        for u in c['pipeline_usage'] if u and u.get('cost') is not None), Decimal('0'))),
                  vision_responses_cost_usd=str(sum((Decimal(str(c['usage']['cost'])) for c in report['cases']
                        if c.get('usage') and c['usage'].get('cost') is not None), Decimal('0'))),
                  pipeline_summary=_summary([c["pipeline_evaluation"] for c in report["cases"]]),
                  vision_summary=_summary([c["vision_evaluation"] for c in report["cases"] if "vision_evaluation" in c]),
                  category_totals={"expected": sum(c["pipeline_category_score"]["expected"] for c in report["cases"]),
                                   "pipeline_correct": sum(c["pipeline_category_score"]["correct"] for c in report["cases"]),
                                   "vision_correct": sum(c.get("vision_category_score", {}).get("correct", 0) for c in report["cases"])})
    write_json(root / "comparison.json", report)
    (root / "comparison.md").write_text(comparison_markdown(report), encoding="utf-8")
    print(f"COMPLETE: {root}; cost={budget.cost} USD", flush=True)
    return int(any(c["status"] == "failed" for c in report["cases"]))


if __name__ == "__main__":
    raise SystemExit(main())
