"""Evaluate parser predictions and replay saved OCR without loading an engine."""

import argparse
import json
from collections import Counter
from decimal import Decimal, InvalidOperation
from difflib import SequenceMatcher
from pathlib import Path

from extraction.contracts import (
    DocumentPage,
    OCRPage,
    OCRResult,
    PreparedDocument,
    ReceiptData,
    SourceFile,
)
from extraction.pipeline import Pipeline
from extraction.reporting import to_dict, write_json, write_result, write_summary
from extraction.steps.ocr.common import fragment
from extraction.steps.validate import ValidateReceiptStep

from .evaluation import NAME_THRESHOLD, normalize_name

ITEM_NUMBERS = ("quantity", "unit_price", "before_discount", "discount", "total")


def _number(value):
    if value is None:
        return None
    try:
        number = Decimal(str(value))
        return number if number.is_finite() else None
    except InvalidOperation:
        return None


def _name_similarity(expected, observed):
    if not expected or not observed:
        return 0.0
    return SequenceMatcher(
        None, normalize_name(expected), normalize_name(observed), autojunk=False
    ).ratio()


def _align(expected, observed, numeric_fields):
    """Ordered alignment preserves repeated products and exposes both kinds of gaps."""
    rows, columns = len(expected), len(observed)
    scores = [[0.0] * (columns + 1) for _ in range(rows + 1)]
    choices = {}
    for i in range(1, rows + 1):
        for j in range(1, columns + 1):
            left, right = expected[i - 1], observed[j - 1]
            similarity = _name_similarity(left.get("name"), right.get("name"))
            numeric_matches = sum(
                _number(left.get(field)) is not None
                and _number(left.get(field)) == _number(right.get(field))
                for field in numeric_fields
            )
            price_match = any(
                _number(left.get(field)) not in (None, Decimal("0"))
                and _number(left.get(field)) == _number(right.get(field))
                for field in ("unit_price", "before_discount", "total")
            )
            options = [(scores[i - 1][j], "missing"), (scores[i][j - 1], "excess")]
            if similarity >= NAME_THRESHOLD or (numeric_matches >= 2 and price_match):
                options.append(
                    (scores[i - 1][j - 1] + 1 + similarity + 0.1 * numeric_matches, "match")
                )
            scores[i][j], choices[i, j] = max(options, key=lambda choice: choice[0])
    matches, missing, excess = [], [], []
    i, j = rows, columns
    while i or j:
        choice = choices.get((i, j), "missing" if i else "excess")
        if choice == "match":
            matches.append((i - 1, j - 1))
            i, j = i - 1, j - 1
        elif choice == "missing":
            missing.append(i - 1)
            i -= 1
        else:
            excess.append(j - 1)
            j -= 1
    return list(reversed(matches)), list(reversed(missing)), list(reversed(excess))


def _check(expected, observed, *, numeric=False, approximate=False):
    similarity = _name_similarity(expected, observed) if approximate else None
    if expected is None:
        status = "not_annotated"
    elif observed is None:
        status = "missing"
    elif numeric:
        left, right = _number(expected), _number(observed)
        status = "match" if left is not None and left == right else "different"
    elif approximate:
        if similarity == 1:
            status = "match"
        else:
            status = "approximate" if similarity >= NAME_THRESHOLD else "different"
    else:
        status = "match" if expected == observed else "different"
    result = {"expected": expected, "observed": observed, "status": status}
    if similarity is not None:
        result["similarity"] = round(similarity, 4)
    return result


def evaluate_receipt(reference, prediction):
    """References are used only after prediction, never by the parsing step."""
    observed = to_dict(prediction) if isinstance(prediction, ReceiptData) else prediction
    case = {
        "source": reference["source"],
        "review_status": reference.get("review_status", "unreviewed"),
        "reference_uncertainties": reference.get("uncertain_fields", []),
        "document_fields": {},
        "groups": {},
    }
    for field in ("store", "date", "currency", "total", "amount_due"):
        case["document_fields"][field] = _check(
            reference.get(field),
            observed.get(field),
            numeric=field in ("total", "amount_due"),
            approximate=field == "store",
        )
        if field in observed.get("evidence", {}):
            case["document_fields"][field]["evidence"] = observed["evidence"][field]
    for group in ("items", "deposits", "deposit_returns"):
        expected_entries, observed_entries = reference.get(group, []), observed.get(group, [])
        numeric_fields = (
            ITEM_NUMBERS if group == "items" else ("quantity", "unit_price", "total")
        )
        annotated = group in reference and reference[group] is not None
        if not annotated:
            expected_entries = []
        matches, missing, excess = (
            _align(expected_entries, observed_entries, numeric_fields)
            if annotated else ([], [], [])
        )
        aligned = []
        matched_indices = dict(matches)
        for index, entry in enumerate(expected_entries):
            observed_index = matched_indices.get(index)
            actual = observed_entries[observed_index] if observed_index is not None else {}
            fields = {
                "name": _check(entry.get("name"), actual.get("name"), approximate=True)
            }
            fields.update(
                {
                    field: _check(entry.get(field), actual.get(field), numeric=True)
                    for field in numeric_fields
                }
            )
            evidence = observed.get("evidence", {})
            for field, check in fields.items():
                key = f"{group}[{observed_index}].{field}"
                if key in evidence:
                    check["evidence"] = evidence[key]
            aligned.append(
                {"expected_index": index, "observed_index": observed_index, "fields": fields}
            )
        case["groups"][group] = {
            "annotated": annotated,
            "expected_count": len(expected_entries),
            "observed_count": len(observed_entries),
            "matched_count": len(matches),
            "missing_indices": missing,
            "excess_indices": excess,
            "entries": aligned,
            "excess_entries": [observed_entries[index] for index in excess],
        }
    return case


def _summary(cases):
    fields, groups = {}, {}
    for case in cases:
        for field, check in case["document_fields"].items():
            fields.setdefault(field, Counter())[check["status"]] += 1
        for group, details in case["groups"].items():
            if not details["annotated"]:
                continue
            counts = groups.setdefault(group, Counter())
            for key in ("expected_count", "observed_count", "matched_count"):
                counts[key] += details[key]
            counts["missing_count"] += len(details["missing_indices"])
            counts["excess_count"] += len(details["excess_indices"])
            for entry in details["entries"]:
                for field, check in entry["fields"].items():
                    fields.setdefault(f"{group}.{field}", Counter())[check["status"]] += 1
    return {"documents": len(cases), "groups": groups, "fields": fields}


def _markdown(report):
    lines = [
        "# Ocena parsera",
        "",
        "Porównanie gotowych predykcji ze wzorcami; nazwy przybliżone nie są zgodnością dokładną.",
        "",
        "| Pole | Zgodne | Przybliżone | Różne | Brak | Bez adnotacji |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for field, counts in report["summary"]["fields"].items():
        statuses = ("match", "approximate", "different", "missing", "not_annotated")
        cells = " | ".join(str(counts.get(status, 0)) for status in statuses)
        lines.append(f"| {field} | {cells} |")
    lines.extend(
        ["", "| Paragon | Wzorzec | Produkty oczekiwane / wynik | Brak / nadmiar |", "|---|---|---:|---:|"]
    )
    for case in report["cases"]:
        group = case["groups"]["items"]
        lines.append(
            f"| {case['source'].replace('|', ' ')} | {case['review_status']} | "
            f"{group['expected_count']} / {group['observed_count']} | "
            f"{len(group['missing_indices'])} / {len(group['excess_indices'])} |"
        )
    lines.extend(
        [
            "",
            "Raport JSON zawiera dopasowanie pozycji, kaucje, zwroty, status sprawdzenia i niepewności wzorców.",
            "Dopasowanie zachowuje kolejność i korzysta z podobieństwa nazwy lub zgodnych liczb; jest heurystyką.",
            "Wzorce niezatwierdzone oraz niepewne pola wymagają ręcznej oceny; zbiorcze liczby nie są certyfikatem jakości.",
        ]
    )
    if report["skipped"]:
        lines.extend(
            ["", "## Pominięte", ""]
            + [f"- {entry['reference']}: {entry['reason']}" for entry in report["skipped"]]
        )
    return "\n".join(lines) + "\n"


def write_parser_evaluation(run_dir, references_dir, results=None):
    run_dir, references_dir = Path(run_dir), Path(references_dir)
    references = sorted(references_dir.glob("*.expected.json"))
    if not references:
        return None
    report = {
        "schema_version": 1,
        "run": run_dir.name,
        "name_threshold": NAME_THRESHOLD,
        "cases": [],
        "skipped": [],
    }
    for path in references:
        try:
            reference = json.loads(path.read_text(encoding="utf-8"))
            source = reference["source"]
            if (
                not isinstance(source, str)
                or Path(source).name != source
                or source in (".", "..")
            ):
                raise ValueError("Reference source must be a filename or receipt folder name")
            if (references_dir / source).is_dir():
                from extraction.runner import MIME_TYPES

                sources = sorted(
                    path.name for path in (references_dir / source).iterdir()
                    if path.is_file() and path.suffix.lower() in MIME_TYPES
                )
                if reference.get("sources") != sources:
                    raise ValueError(
                        "Reference sources must match the receipt folder's ordered files"
                    )
            if results is not None and source in results:
                outcome = results[source]
                if not outcome.ok or not isinstance(outcome.output, ReceiptData):
                    raise ValueError("No successful parsed receipt")
                prediction = outcome.output
            else:
                raw = json.loads((run_dir / source / "result.json").read_text(encoding="utf-8"))
                prediction = raw["output"]
                if (
                    raw.get("error")
                    or not isinstance(prediction, dict)
                    or "items" not in prediction
                    or "pages" in prediction
                ):
                    raise ValueError("No successful parsed receipt")
            report["cases"].append(evaluate_receipt(reference, prediction))
        except (OSError, ValueError, KeyError, TypeError, ArithmeticError) as exc:
            report["skipped"].append({"reference": path.name, "reason": str(exc)})
    report["summary"] = _summary(report["cases"])
    report["reviewed_summary"] = _summary(
        [case for case in report["cases"] if case["review_status"] == "reviewed"]
    )
    output_dir = run_dir / "parsing-evaluation"
    output_dir.mkdir(exist_ok=False)
    write_json(output_dir / "report.json", report)
    (output_dir / "report.md").write_text(_markdown(report), encoding="utf-8")
    return output_dir / "report.md"


def _restore_ocr(raw):
    metadata = raw["document"]
    document = PreparedDocument(
        tuple(SourceFile(b"", entry["mime_type"], entry["name"]) for entry in metadata["sources"]),
        tuple(DocumentPage(content=b"", **entry) for entry in metadata["pages"]),
    )
    pages = tuple(
        OCRPage(
            **{key: value for key, value in page.items() if key != "lines"},
            lines=tuple(
                fragment(line["text"], line["confidence"], line["box"])
                for line in page["lines"]
            ),
        )
        for page in raw["pages"]
    )
    return OCRResult(
        document, pages, raw["engine"], raw["version"], raw["settings"],
        raw.get("coordinate_system", OCRResult.__dataclass_fields__["coordinate_system"].default),
    )


def replay_saved_ocr(ocr_run_dir, references_dir, output_dir):
    from extraction.steps.parsing import ParseReceiptStep

    ocr_run_dir, output_dir = Path(ocr_run_dir), Path(output_dir)
    sources = []
    for folder in sorted(ocr_run_dir.iterdir()):
        if not folder.is_dir():
            continue
        path = folder / "ocr.json"
        if not path.is_file():
            path = folder / "result.json"
        if path.is_file():
            raw = json.loads(path.read_text(encoding="utf-8"))
            raw = raw.get("output", raw)
            if isinstance(raw, dict) and raw.get("kind") == "ocr":
                sources.append((folder.name, _restore_ocr(raw)))
    if not sources:
        raise ValueError("No saved OCR documents found")
    output_dir.mkdir(parents=True, exist_ok=False)
    pipeline = Pipeline([("parse", ParseReceiptStep()), ("validate", ValidateReceiptStep())])
    results = {}
    for name, ocr in sources:
        result = pipeline.run(ocr)
        results[name] = result
        result.captured_steps["ocr"] = ocr
        write_result(output_dir / name, result, save_images=False)
    write_summary(output_dir, results)
    write_parser_evaluation(output_dir, references_dir, results)
    return results


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("ocr_run_dir", type=Path)
    parser.add_argument("references_dir", type=Path)
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()
    replay_saved_ocr(args.ocr_run_dir, args.references_dir, args.output_dir)
