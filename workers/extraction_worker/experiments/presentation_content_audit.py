"""Diagnostic: evaluate unchanged Gemini field values separately from evidence validation."""

import copy
import json
import sys
from pathlib import Path

from extraction.contracts import ReceiptData
from extraction.reporting import to_dict, write_json
from extraction.steps.parsing.gemini import decode_receipt
from .parsing import _markdown, _restore_ocr, _summary, evaluate_receipt


def main(root, gemini_run):
    root, gemini_run = Path(root), Path(gemini_run)
    inputs = Path(__file__).resolve().parents[1] / "data" / "input"
    refs = {reference["source"]: reference for path in inputs.glob("*.expected.json")
            for reference in [json.loads(path.read_text(encoding="utf-8"))]}
    cases, diagnostics = [], []
    sources = json.loads((gemini_run / "gemini-run.json").read_text(encoding="utf-8"))["sources"]
    for name in sources:
        folder = gemini_run / name
        strict = json.loads((folder / "result.json").read_text(encoding="utf-8"))
        error, prediction, evidence_count = None, ReceiptData(), 0
        try:
            response = json.loads((folder / "api-response.json").read_text(encoding="utf-8"))
            candidate, = response["candidates"]
            if candidate.get("finishReason") != "STOP":
                raise ValueError("Incomplete API response")
            raw = json.loads("".join(part.get("text", "") for part in candidate["content"]["parts"]
                                     if not part.get("thought")))
            evidence_count = len(raw["evidence"])
            raw = copy.deepcopy(raw)
            raw["evidence"] = []
            ocr = _restore_ocr(json.loads((root / "oriented" / name / "ocr.json").read_text(encoding="utf-8")))
            prediction = decode_receipt(raw, ocr).output
        except (OSError, ValueError, KeyError, TypeError) as exc:
            error = str(exc)
        cases.append(evaluate_receipt(refs[name], prediction))
        diagnostics.append({"source": name, "strict_error": strict.get("error"),
                            "content_error": error, "original_evidence_entries": evidence_count})
    report = {"schema_version": 1, "run": str(gemini_run), "cases": cases, "skipped": [],
              "summary": _summary(cases),
              "reviewed_summary": _summary([case for case in cases if case["review_status"] == "reviewed"]),
              "diagnostics": diagnostics,
              "method": "Offline diagnostic only: discard evidence array before decoding the SAME API responses. "
                        "Do not change any field value, make new API calls or replace strict integration results."}
    output = root / "gemini-content-evaluation"
    output.mkdir(exist_ok=True)
    write_json(output / "report.json", report)
    (output / "report.md").write_text(
        "# Diagnostyka treści odpowiedzi Gemini\n\n"
        "Wyłącznie analiza pomocnicza: usunięto evidence z zapisanych odpowiedzi przed dekodowaniem. "
        "Nie zmieniono wartości pól ani wyników właściwego eksperymentu. "
        "Ten wynik nie dowodzi poprawności przypisanych źródeł ani gotowości adaptera.\n\n" + _markdown(report),
        encoding="utf-8")
    print(output)


if __name__ == "__main__":
    main(*sys.argv[1:])
