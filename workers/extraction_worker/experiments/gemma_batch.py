"""Small Gemma enrichment benchmark: 2–5 receipts with bounded API attempts."""

import argparse
import json
from decimal import Decimal
from pathlib import Path
from time import perf_counter, sleep

from extraction.providers import GoogleClient, NvidiaClient, OpenRouterClient, ProviderHTTPError
from extraction.reporting import to_dict, write_json
from extraction.categories import TAXONOMY_VERSION
from extraction.runner import new_run
from extraction.analysis.item_classifier import CATEGORIES, ReceiptItemClassifier, build_request
from extraction.requests import dispatch_request
from extraction.steps.parsing import ParseReceiptStep
from .gemma import WORKER_DIR, load_config
from .parsing import _restore_ocr, evaluate_receipt

SOURCES = (
    "Screenshot_20261011_140209_appka.jpg",  # Three products, app screenshot.
    "IMG_6796.JPEG",                       # Food and household paper.
    "20261011_130153.jpg",                 # Alcohol.
    "IMG_6793.JPEG",                       # More difficult OCR, partial items.
    "IMG_6885.JPEG",                       # Several product categories.
)


def _normalized(value):
    return " ".join(value.casefold().split()) if isinstance(value, str) else None


def evaluate_review(cases, review):
    """Score only explicitly reviewed labels, against frozen input item IDs."""
    if (not isinstance(review, dict) or "cases" not in review
            or set(review) - {"cases", "taxonomy_version", "reviewer", "review_notes"}
            or not isinstance(review["cases"], list)):
        raise ValueError("Review must contain a cases array")
    if review.get("taxonomy_version", TAXONOMY_VERSION) != TAXONOMY_VERSION:
        raise ValueError("Review taxonomy version does not match current taxonomy")
    known = {case["source"]: case for case in cases}
    metrics = {"names_scored": 0, "names_correct_before": 0, "names_correct_after": 0,
               "names_improved": 0, "names_regressed": 0,
               "categories_scored": 0, "categories_correct": 0}
    metrics.update(review_requested=0, uncertainty_status_missing=0,
                   auto_categories_scored=0, auto_categories_correct=0)
    seen = set()
    for case_review in review["cases"]:
        source = case_review["source"]
        if source not in known or source in seen:
            raise ValueError("Unknown or duplicate source in review")
        seen.add(source)
        case = known[source]
        seen_ids = set()
        for entry in case_review["items"]:
            index = entry["id"]
            if (type(index) is not int or index < 0 or index >= len(case["parser"]["items"])
                    or index in seen_ids):
                raise ValueError("Invalid or duplicate item id in review")
            seen_ids.add(index)
            if "name_raw" in entry and entry["name_raw"] != case["parser"]["items"][index]["name"]:
                raise ValueError("Review name does not match frozen parser input")
            if type(entry.get("reviewed")) is not bool:
                raise ValueError("Review flag must be a boolean")
            if not entry["reviewed"]:
                continue
            names, category = entry.get("accepted_names"), entry.get("category")
            if (names is not None and (not isinstance(names, list) or not names
                                      or any(not isinstance(n, str) or not n.strip() for n in names))):
                raise ValueError("Accepted names must be a nonempty string array or null")
            if category is not None and category not in CATEGORIES:
                raise ValueError("Unknown expected category")
            if case["status"] != "completed":
                continue  # No model result means no accuracy claim.
            before = case["parser"]["items"][index]["name"]
            after = case["enrichment"]["items"][index]
            metrics["review_requested"] += after.get("needs_review") is True
            metrics["uncertainty_status_missing"] += type(after.get("needs_review")) is not bool
            if names is not None:
                accepted = {_normalized(name) for name in names}
                correct_before = _normalized(before) in accepted
                correct_after = _normalized(after["name_corrected"]) in accepted
                metrics["names_scored"] += 1
                metrics["names_correct_before"] += correct_before
                metrics["names_correct_after"] += correct_after
                metrics["names_improved"] += not correct_before and correct_after
                metrics["names_regressed"] += correct_before and not correct_after
            if category is not None:
                metrics["categories_scored"] += 1
                metrics["categories_correct"] += after["category"] == category
                if after.get("needs_review") is False:
                    metrics["auto_categories_scored"] += 1
                    metrics["auto_categories_correct"] += after["category"] == category
    for field, numerator, denominator in (
        ("name_accuracy_before", "names_correct_before", "names_scored"),
        ("name_accuracy_after", "names_correct_after", "names_scored"),
        ("category_accuracy", "categories_correct", "categories_scored"),
        ("auto_category_accuracy", "auto_categories_correct", "auto_categories_scored"),
    ):
        metrics[field] = metrics[numerator] / metrics[denominator] if metrics[denominator] else None
    return metrics


def _cell(value):
    return str(value if value is not None else "—").replace("|", "\\|").replace("\n", " ")


def render_report(report):
    metrics = report["accuracy"]
    lines = ["# Mały eksperyment Gemma", "",
             f"Dokumenty: {len(report['cases'])}; wywołania API: {report['api_calls']}; "
             f"maksymalny limit wywołań: {report.get('max_calls', report['limit'])}.", "",
             "Jedno żądanie obejmuje wszystkie produkty paragonu. "
             f"Ponowienie po HTTP 500/502/503/504: {report.get('retry_server_error', False)}; "
             f"przerwa po żądaniu: {report.get('min_interval', 0)} s. "
             "Maksymalnie jedno ponowienie na dokument, wliczone do limitu; "
             "nieobsłużony błąd zatrzymuje dalsze wywołania.", "",
             "Dokładność dotyczy wyłącznie ręcznie zatwierdzonych etykiet i ukończonych odpowiedzi. "
             "Nazwy porównujemy po ujednoliceniu wielkości liter i białych znaków. "
             "Próbka jest diagnostyczna, nie reprezentatywna.", ""]
    for label, numerator, denominator in (
        ("Nazwy przed", "names_correct_before", "names_scored"),
        ("Nazwy po", "names_correct_after", "names_scored"),
        ("Kategorie", "categories_correct", "categories_scored"),
    ):
        count = metrics[denominator]
        score = f"{metrics[numerator]}/{count} ({100 * metrics[numerator] / count:.1f}%)" if count else "brak ocenionych etykiet"
        lines.append(f"- {label}: {score}")
    lines.extend([f"- Poprawione nazwy: {metrics['names_improved']}; pogorszone: {metrics['names_regressed']}", ""])
    lines.extend([f"Status niepewności w ocenionych pozycjach: {metrics.get('review_requested', 0)} "
                  f"do sprawdzenia; {metrics.get('uncertainty_status_missing', 0)} bez danych o statusie.", ""])
    if report.get("paid"):
        lines.extend([f"Budżet eksperymentu: {report['budget_usd']} USD; "
                      f"konserwatywna rezerwacja: {report['reserved_cost_usd']} USD; "
                      f"koszt zgłoszony przez API dla nowych odpowiedzi: {report['reported_cost_usd']} USD.",
                      "Rezerwacja jest oszacowaniem przy ograniczonej cenie routingu, nie limitem rozliczeniowym dostawcy. "
                      "Brak pola usage.cost nie potwierdza zerowego kosztu; cache nie generuje nowego kosztu API.", ""])
    for case in report["cases"]:
        lines.extend([f"## {_cell(case['source'])}", "", f"Status: {case['status']}", ""])
        if case.get("error"):
            lines.extend([case["error"], ""])
        if "cache_hit" in case:
            lines.extend([f"Cache: {case['cache_hit']}; próby API: {len(case.get('attempts', []))}; "
                          f"czas: {case.get('seconds', 0)} s.", ""])
        for attempt in case.get("attempts", []):
            if attempt.get("http_status"):
                lines.extend([f"Próba {attempt['number']}: HTTP {attempt['http_status']}.", ""])
        if case.get("parser_evaluation"):
            group = case["parser_evaluation"]["groups"]["items"]
            lines.extend([f"Parser vs istniejący wzorzec ({case['parser_evaluation']['review_status']}): "
                          f"{group['matched_count']} dopasowanych, {len(group['missing_indices'])} brakujących, "
                          f"{len(group['excess_indices'])} nadmiarowych. Dopasowanie jest heurystyczne; "
                          "wzorzec nie jest wzorcem korekty nazw ani kategorii.", ""])
        lines.extend(["| ID | Parser | Gemma | Kategoria | Do sprawdzenia |", "|---|---|---|---|---|"])
        for i, item in enumerate(case["parser"]["items"]):
            enriched = case.get("enrichment", {}).get("items", [])
            after = enriched[i] if i < len(enriched) else {}
            review_state = (after.get("review_reason") if after.get("needs_review") is True else
                            "nie" if after.get("needs_review") is False else "brak danych")
            lines.append(f"| {i} | {_cell(item['name'])} | {_cell(after.get('name_corrected'))} | {_cell(after.get('category'))} | {_cell(review_state)} |")
        lines.append("")
    return "\n".join(lines)


def run_batch(*, limit=3, execute=False, output_dir=None, cases=None, classifier=None, provider="openrouter",
              max_calls=None, retry_server_error=False, min_interval=0, paid=False, max_cost_usd="0.10"):
    if type(limit) is not int or not 2 <= limit <= 5:
        raise ValueError("Choose 2–5 receipts")
    if provider not in ("openrouter", "google", "nvidia"):
        raise ValueError("Choose openrouter, google or nvidia")
    budget = Decimal(str(max_cost_usd))
    if not budget.is_finite() or not Decimal(0) < budget <= Decimal("0.10"):
        raise ValueError("Experiment budget must be positive and no more than USD 0.10")
    if paid and provider != "openrouter":
        raise ValueError("Paid mode is supported only through OpenRouter")
    if (isinstance(classifier, ReceiptItemClassifier) and classifier.allow_paid and classifier.provider == "openrouter") and not paid:
        raise ValueError("Paid classifier requires explicit paid mode")
    max_calls = limit if max_calls is None else max_calls
    if type(max_calls) is not int or not 1 <= max_calls <= 5:
        raise ValueError("Choose an API attempt cap of 1–5")
    if not isinstance(min_interval, (int, float)) or not 0 <= min_interval <= 60:
        raise ValueError("Request interval must be between 0 and 60 seconds")
    selected = []
    if cases is None:
        for source in SOURCES[:limit]:
            paths = list((WORKER_DIR / "data/output").glob(f"*/{source}/ocr.json"))
            paths += list((WORKER_DIR / "data/output").glob(f"*/*/{source}/ocr.json"))
            if not paths:
                raise ValueError(f"No saved OCR for {source}")
            path = max(paths, key=lambda p: p.stat().st_mtime)
            ocr = _restore_ocr(json.loads(path.read_text(encoding="utf-8")))
            selected.append((source, ocr, str(path.resolve())))
    else:
        if len(cases) != limit:
            raise ValueError("Case count must match limit")
        selected = cases
    config = load_config() if provider == "openrouter" else None
    if paid:
        if classifier is not None and not (isinstance(classifier, ReceiptItemClassifier) and classifier.allow_paid and classifier.provider == "openrouter"):
            raise ValueError("Paid mode requires the bounded paid OpenRouter adapter")
        if classifier is None:
            settings = {"api_key": config["api_key"] if execute else "dry-run",
                        "base_url": config["base_url"]}
            classifier = ReceiptItemClassifier(
                OpenRouterClient(settings["api_key"], base_url=settings["base_url"]),
                allow_paid=True,
                cache_dir=WORKER_DIR / ".cache/gemma-openrouter-paid",
            )
    elif classifier is None and provider == "nvidia":
        settings = load_config(provider="nvidia") if execute else {"api_key": "dry-run"}
        classifier = ReceiptItemClassifier(NvidiaClient(settings["api_key"]), cache_dir=WORKER_DIR / ".cache/gemma-nvidia")
    elif classifier is None and provider == "google":
        from .gemini import load_api_key
        # A dry run needs no key; it only builds the public request payload.
        classifier = ReceiptItemClassifier(
            GoogleClient(load_api_key() if execute else "dry-run"),
            cache_dir=WORKER_DIR / ".cache/gemma-google",
        )
    elif execute and classifier is None:
        classifier = ReceiptItemClassifier(
            OpenRouterClient(config["api_key"], base_url=config["base_url"]),
            cache_dir=WORKER_DIR / ".cache/gemma",
            model=config.get("model", FREE_MODEL),
        )
    root = new_run(output_dir or WORKER_DIR / "data/output")
    report = {"limit": limit, "max_calls": max_calls, "retry_server_error": retry_server_error,
              "min_interval": min_interval, "execute": execute, "provider": provider,
              "taxonomy_version": TAXONOMY_VERSION, "paid": paid, "budget_usd": str(budget) if paid else None,
              "reserved_cost_usd": "0", "reported_cost_usd": "0", "api_calls": 0, "cases": []}
    prepared = []
    review = {"taxonomy_version": TAXONOMY_VERSION, "cases": []}
    # Parse and save every planned case before any network access.
    for source, ocr, origin in selected:
        parsed = ParseReceiptStep().run(ocr)
        receipt = parsed.output
        folder = root / source
        folder.mkdir()
        payload = classifier.build_request(receipt, ocr) if classifier else build_request(receipt, ocr, config["model"])
        write_json(folder / "request.json", payload)
        case = {"source": source, "ocr_path": origin, "status": "prepared",
                "parser": to_dict(receipt), "parser_warnings": to_dict(parsed.warnings)}
        if paid:
            case["request_bound_usd"] = str(classifier.request_bound(payload))
        ref_path = WORKER_DIR / "data/input" / (Path(source).stem + ".expected.json")
        if ref_path.is_file():
            reference = json.loads(ref_path.read_text(encoding="utf-8"))
            if reference.get("source") != source:
                raise ValueError("Reference source does not match OCR source")
            case["parser_evaluation"] = evaluate_receipt(reference, receipt)
        review["cases"].append({"source": source, "items": [
            {"id": i, "name_raw": item.name, "accepted_names": None,
             "category": None, "reviewed": False} for i, item in enumerate(receipt.items)]})
        report["cases"].append(case)
        prepared.append((case, receipt, ocr, folder))
    write_json(root / "review.template.json", review)
    if paid:
        # Reserve for the full attempt cap, even when some responses may be cached.
        preflight = max(Decimal(c["request_bound_usd"]) for c in report["cases"]) * max_calls
        report["preflight_bound_usd"] = str(preflight)
        if preflight > budget:
            raise ValueError("Conservative preflight reservation exceeds the experiment budget; no API calls made")
    halted = False
    next_request_at = 0
    for case, receipt, ocr, folder in prepared:
        if not execute or halted:
            if halted:
                case["status"] = "skipped_after_error"
            continue
        start = perf_counter()
        # Count attempts, including failed ones, at the transport boundary.
        previous_policy = classifier.request_policy
        case["attempts"] = []
        def counted(body, send):
            nonlocal next_request_at
            for number in range(1, 3 if retry_server_error else 2):
                if report["api_calls"] >= max_calls:
                    raise RuntimeError("API request cap reached; no further request was sent")
                if paid:
                    reserved = Decimal(report["reserved_cost_usd"]) + Decimal(case["request_bound_usd"])
                    if reserved > budget:
                        raise RuntimeError("Experiment budget reached; no further request was sent")
                    report["reserved_cost_usd"] = str(reserved)
                delay = next_request_at - perf_counter()
                if delay > 0:
                    sleep(delay)
                report["api_calls"] += 1
                attempt = {"number": number, "status": "failed"}
                case["attempts"].append(attempt)
                try:
                    response = dispatch_request(body, send, previous_policy)
                    attempt["status"] = "completed"
                    return response
                except ProviderHTTPError as exc:
                    attempt["http_status"] = exc.status
                    if (not retry_server_error or number == 2 or exc.status not in (500, 502, 503, 504)
                            or report["api_calls"] >= max_calls):
                        raise
                finally:
                    next_request_at = perf_counter() + min_interval
        classifier.request_policy = counted
        try:
            enrichment = classifier.run(receipt, ocr)
            if not receipt.items:
                case["status"] = "empty_parser_output"
            else:
                case.update(status="completed", enrichment=enrichment, cache_hit=classifier.cache_hit,
                            usage=(classifier.response or {}).get("usage"),
                            provider=(classifier.response or {}).get("provider"))
                write_json(folder / "enrichment.json", enrichment)
        except (RuntimeError, ValueError, OSError) as exc:
            case.update(status="failed", error=str(exc))
            halted = True
        finally:
            classifier.request_policy = previous_policy
        # Preserve a completed provider response even if local JSON validation failed.
        # Failed HTTP response bodies are never retained by the adapter.
        if classifier.response is not None:
            write_json(folder / "response.json", classifier.response)
            case.setdefault("cache_hit", classifier.cache_hit)
            case.setdefault("usage", classifier.response.get("usage"))
            case.setdefault("provider", classifier.response.get("provider"))
            cost = (classifier.response.get("usage") or {}).get("cost")
            if cost is not None and not classifier.cache_hit:
                numeric_cost = Decimal(str(cost))
                if numeric_cost.is_finite() and numeric_cost >= 0:
                    report["reported_cost_usd"] = str(Decimal(report["reported_cost_usd"]) + numeric_cost)
        case["seconds"] = round(perf_counter() - start, 3)
    report["accuracy"] = evaluate_review(report["cases"], review)
    write_json(root / "report.json", report)
    (root / "report.md").write_text(render_report(report), encoding="utf-8")
    return root, report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--limit", type=int, choices=range(2, 6), default=3)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--provider", choices=("openrouter", "google", "nvidia"), default="openrouter")
    parser.add_argument("--max-calls", type=int, choices=range(1, 6), help="Hard API attempt cap, including retries")
    parser.add_argument("--retry-server-error", action="store_true", help="Retry HTTP 500/502/503/504 once per case")
    parser.add_argument("--min-interval", type=float, default=0, help="Seconds between network attempts (0–60)")
    parser.add_argument("--paid", action="store_true", help="Explicit opt-in to paid OpenRouter Gemma")
    parser.add_argument("--max-cost-usd", default="0.10", help="Conservative reservation cap, at most USD 0.10")
    parser.add_argument("--evaluate", type=Path, help="Existing run; evaluation only, no API")
    parser.add_argument("--review", type=Path, help="Completed review JSON")
    args = parser.parse_args()
    if args.evaluate:
        if args.execute or not args.review:
            parser.error("--evaluate requires --review and cannot use --execute")
        report = json.loads((args.evaluate / "report.json").read_text(encoding="utf-8"))
        report["accuracy"] = evaluate_review(report["cases"], json.loads(args.review.read_text(encoding="utf-8")))
        write_json(args.evaluate / "reviewed-report.json", report)
        (args.evaluate / "reviewed-report.md").write_text(render_report(report), encoding="utf-8")
        print(json.dumps(report["accuracy"], ensure_ascii=False, indent=2))
        return 0
    if args.review:
        parser.error("--review requires --evaluate")
    root, report = run_batch(limit=args.limit, execute=args.execute, provider=args.provider,
                             max_calls=args.max_calls, retry_server_error=args.retry_server_error,
                             min_interval=args.min_interval, paid=args.paid, max_cost_usd=args.max_cost_usd)
    print(f"Results: {root}")
    print(f"API calls: {report['api_calls']}/{report['max_calls']}")
    for case in report["cases"]:
        print(f"{case['source']}: {case['status']}")
    return int(any(c["status"] == "failed" for c in report["cases"]))


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, RuntimeError) as exc:
        print(f"Cannot run Gemma benchmark: {exc}")
        raise SystemExit(1) from None
