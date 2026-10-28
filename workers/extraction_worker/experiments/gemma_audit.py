"""Offline evaluation of saved enrichment; never imports credentials or calls API."""

import argparse
import copy
import json
from pathlib import Path

from extraction.categories import TAXONOMY_VERSION
from extraction.reporting import write_json
from extraction.runner import new_run
from .gemma_batch import evaluate_review, render_report

# Explicit renames/merges only, never reclassify a prediction from its product name.
LEGACY_CATEGORY_MAP = {
    "żywność": "żywność i napoje",
    "napoje bezalkoholowe": "żywność i napoje",
    "alkohol": "alkohol i tytoń",
    "higiena i kosmetyki": "kosmetyki i higiena",
}


def audit_saved_run(run_dir, review_file, output_dir):
    run_dir, review_file = Path(run_dir), Path(review_file)
    original = json.loads((run_dir / "report.json").read_text(encoding="utf-8"))
    review = json.loads(review_file.read_text(encoding="utf-8"))
    if original.get("taxonomy_version") not in (None, "1", TAXONOMY_VERSION):
        raise ValueError("Unsupported saved taxonomy version")
    cases = copy.deepcopy(original["cases"])
    legacy = original.get("taxonomy_version") is None
    for case in cases:
        for item in case.get("enrichment", {}).get("items", []):
            if legacy:
                item["legacy_category"] = item["category"]
                item["category"] = LEGACY_CATEGORY_MAP.get(item["category"], item["category"])
                # Historical outputs had no uncertainty status: never imply automatic approval.
                item.pop("needs_review", None)
                item.pop("review_reason", None)
    report = {**original, "cases": cases, "execute": False, "paid": False, "api_calls": 0,
              "source_run": str(run_dir.resolve()),
              "source_taxonomy_version": original.get("taxonomy_version"),
              "taxonomy_version": review.get("taxonomy_version", TAXONOMY_VERSION),
              "legacy_category_mapping_applied": legacy, "legacy_category_mapping": LEGACY_CATEGORY_MAP if legacy else {},
              "reviewer": review.get("reviewer", "unspecified"),
              "review_notes": review.get("review_notes", ""),
              "original_run_reported_cost_usd": original.get("reported_cost_usd"),
              "accuracy": evaluate_review(cases, review)}
    root = new_run(output_dir)
    write_json(root / "audit.json", report)
    write_json(root / "review.json", review)
    text = ("# Ocena offline zapisanych wyników\n\n"
            f"Źródło: {run_dir.resolve()}\n\n"
            "Nowe wywołania API: **0**. Oryginalnego przebiegu nie zmieniono.\n\n"
            f"Oceniający: {report['reviewer']}. {report['review_notes']}\n\n"
            "Stare nazwy kategorii połączono/przemianowano jawnie; nie poprawiano "
            "samych decyzji modelu. Nie jest to wynik nowego promptu. "
            "Historyczne odpowiedzi nie zawierają statusu niepewności.\n\n")
    (root / "audit.md").write_text(text + render_report(report), encoding="utf-8")
    return root, report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run_dir", type=Path)
    parser.add_argument("--review", type=Path, default=Path(__file__).parent / "fixtures/gemma-small-review-v1.json")
    parser.add_argument("--output-dir", type=Path, default=Path(__file__).resolve().parents[1] / "data/output")
    args = parser.parse_args()
    root, report = audit_saved_run(args.run_dir, args.review, args.output_dir)
    print(f"Offline audit: {root}")
    print(json.dumps(report["accuracy"], ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
