"""Evaluate this comparison's saved results and write JSON and Markdown reports."""

import json
from pathlib import Path

from extraction.references import validate_reference
from extraction.reporting import write_json

from .evaluation import audit_receipt
from .ocr_metrics import evaluate_cer, reconstruct_rows, summarize_variants, transcription_text


def _load_references(directory):
    references, invalid = {}, []
    for path in sorted(directory.glob("*.expected.json")):
        try:
            reference = json.loads(path.read_text(encoding="utf-8"))
            errors = validate_reference(reference)
            if errors:
                raise ValueError("; ".join(errors))
            source = reference["source"]
            if Path(source).name != source or source in (".", ".."):
                raise ValueError("Reference source must be a filename or receipt folder name")
            if source in references:
                raise ValueError("Duplicate reference source")
            text_path = path.with_suffix(".txt")
            expected = text_path.read_text(encoding="utf-8") if text_path.is_file() else None
            references[source] = reference, expected
        except (OSError, ValueError, TypeError, KeyError) as exc:
            invalid.append({"reference": path.name, "reason": str(exc)})
    return references, invalid


def _evaluate_case(path, source, reference, grouped, process_error):
    case = {
        "source": source, "status": "failed", "reference_status": "unreviewed",
        "cer": {"status": "not_evaluated", "reason": "No successful OCR"},
    }
    if not path.is_file() and process_error:
        case["error"] = process_error
        return case
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
        if payload.get("error"):
            case["error"] = payload["error"]
            return case
        raw = payload["output"]
        if raw.get("kind") != "ocr":
            raise ValueError("No OCR output")
        view = reconstruct_rows(raw)
        case.update(status="ok", text=transcription_text(view), pages=view["pages"],
                    result_path=str(path.resolve()))
        if reference is None:
            raise ValueError("Missing valid expected JSON")
        expected, expected_text = reference
        case["reference_status"] = "reviewed" if expected.get("review_status") == "reviewed" else "unreviewed"
        sources = [item["name"] for item in raw["document"]["sources"]]
        if "sources" in expected and expected["sources"] != sources:
            raise ValueError("Reference sources differ from the input order")
        if (grouped or len(sources) > 1) and "sources" not in expected:
            raise ValueError("Multi-source receipt requires ordered reference sources")
        case["audit"] = audit_receipt(expected, view)
        case["cer"] = evaluate_cer(expected, expected_text, case["text"])
    except (OSError, ValueError, TypeError, KeyError, ArithmeticError, IndexError) as exc:
        case["reference_error"] = str(exc)
        case["cer"] = {"status": "not_evaluated", "reason": str(exc)}
    return case


def write_comparison(root, input_dir, documents, states, repeats):
    references, invalid = _load_references(input_dir)
    variants = {}
    for variant, state in states.items():
        variants[variant] = {
            "execution": state,
            "cases": [
                _evaluate_case(root / variant / source / "result.json", source,
                               references.get(source), (input_dir / source).is_dir(), state.get("error"))
                for source in documents
            ],
        }
    common_timings, common_cases = summarize_variants(variants)
    reviewed = sum(value["groups"]["reviewed"]["case_count"] for value in variants.values())
    unreviewed = sum(value["groups"]["unreviewed"]["case_count"] for value in variants.values())
    report = {
        "variants": variants, "invalid_references": invalid,
        "common_timing_cases": common_timings, "common_cases": common_cases,
        "provisional": bool(unreviewed) or not reviewed,
        "method": {"repeats": repeats, "timing_is_single_sample": repeats == 1,
                   "row_tolerance_in_median_heights": 0.5, "audit_is_heuristic": True,
                   "normalization": "NFC; whitespace collapsed; case and punctuation preserved"},
    }
    write_json(root / "comparison.json", report)
    with (root / "comparison.md").open("x", encoding="utf-8") as stream:
        stream.write(_markdown(report))
    return report


def _cell(value):
    return str(value).replace("|", "\\|").replace("\n", " ")


def _number(value):
    return "nie oceniono" if value is None else f"{value:.4f}"


def _markdown(report):
    lines = [
        "# Porównanie lokalnych OCR", "",
        "Audyt nazw i liczb jest heurystyką; brak przypisania nie dowodzi utraty tekstu. "
        "Niezatwierdzone wzorce dają wstępne wyniki. CER wymaga pełnej transkrypcji.",
        "Confidence różnych silników nie jest porównywane. Paddle i RapidOCR współdzielą "
        "rodzinę modeli, ale różnią się również przetwarzaniem obrazu.",
        "Czas obejmuje adapter OCR i konwersję wyniku, bez przygotowania, oceny i zapisu. "
        "Inicjalizacja i rozgrzewka są osobne; Tesseract ładuje modele dla każdej strony.",
        "Pomiar: " + ("jeden odczyt, wynik orientacyjny." if report["method"]["timing_is_single_sample"]
                      else "mediana powtórzeń; nieudane pomiary nie mają mediany."), "",
        f"Wspólny zestaw pomiaru czasu: {len(report['common_timing_cases'])} dokumentów.", "",
        "| Wariant | Dostępność | Poprawne dokumenty | Inicjalizacja [s] | Rozgrzewka [s] | Mediana własna / wspólna [s] |",
        "|---|---|---:|---:|---:|---:|",
    ]
    for name, value in report["variants"].items():
        state = value["execution"]
        lines.append(
            f"| {name} | {state['status']} | {value['successful_documents']}/{len(value['cases'])} "
            f"| {_number(state['initialization_seconds'])} | {_number(state['warmup_seconds'])} "
            f"| {_number(value['median_document_seconds'])} / {_number(value['common_median_document_seconds'])} |"
        )
        error = state.get("error") or state.get("warmup_error")
        if error:
            lines.append(f"\n{name}: {_cell(error)}\n")
    for status, label in (("reviewed", "Sprawdzone wzorce"), ("unreviewed", "Niezatwierdzone wzorce")):
        common = report["common_cases"][status]
        lines.extend([
            "", f"## {label}", "",
            f"Wspólne przypadki audytu: {len(common['audit'])}; CER: {len(common['cer'])}. "
            "Uwzględniono warianty o statusie available.", "",
            "| Wariant | Własne pokrycie audytu / CER | Wspólne nazwy zgodne / przybliżone / brak | Wspólne liczby zgodne / inne | Wspólny CER |",
            "|---|---:|---|---|---:|",
        ])
        for name, value in report["variants"].items():
            own = value["groups"][status]
            shared = value["common_groups"][status]
            names = shared["audit"]["name_counts"]
            numbers = shared["audit"]["numeric_field_counts"]
            lines.append(
                f"| {name} | {own['case_count']} / {own['cer_case_count']} "
                f"| {names.get('match', 0)} / {names.get('approximate', 0)} / {names.get('missing', 0)} "
                f"| {numbers.get('match', 0)} / {sum(numbers.values()) - numbers.get('match', 0)} "
                f"| {_number(shared['cer']['cer'])} |"
            )
    lines.extend([
        "", "## Dokumenty", "",
        "| Wariant | Dokument | OCR | Wzorzec | Nazwy zgodne / przybliżone / brak | Liczby zgodne / inne | CER | Czas [s] | Uwagi |",
        "|---|---|---|---|---|---|---:|---:|---|",
    ])
    for name, value in report["variants"].items():
        for case in value["cases"]:
            audit = case.get("audit", {})
            names, numbers = audit.get("name_counts", {}), audit.get("numeric_field_counts", {})
            timing = value["execution"]["timings"].get(case["source"], {}).get("median_seconds")
            runs = value["execution"]["timings"].get(case["source"], {}).get("runs", [])
            timing_error = next((run["error"] for run in runs if not run["ok"]), None)
            note = case.get("error") or timing_error or case.get("reference_error") or case["cer"].get("reason", "")
            lines.append(
                f"| {name} | {_cell(case['source'])} | {case['status']} | {case['reference_status']} "
                f"| {names.get('match', 0)} / {names.get('approximate', 0)} / {names.get('missing', 0)} "
                f"| {numbers.get('match', 0)} / {sum(numbers.values()) - numbers.get('match', 0)} "
                f"| {_number(case['cer'].get('cer'))} | {_number(timing)} | {_cell(note)} |"
            )
    if report["invalid_references"]:
        lines.extend(["", "## Niepoprawne wzorce", ""])
        lines.extend(f"- {_cell(item['reference'])}: {_cell(item['reason'])}"
                     for item in report["invalid_references"])
    lines.extend(["", "Oryginalne fragmenty i ramki: result.json wariantu. "
                  "Rekonstrukcja wierszy, dowody audytu i metryki: comparison.json.", ""])
    return "\n".join(lines)
