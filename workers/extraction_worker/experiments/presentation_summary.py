"""Summarize saved full-dataset presentation experiments without loading models."""

import csv
import json
import sys
from collections import Counter
from pathlib import Path
from statistics import median


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def numeric_score(case):
    return sum(check["status"] == "match" for entry in case["groups"]["items"]["entries"]
               for field, check in entry["fields"].items() if field != "name")


def main(root):
    root = Path(root)
    orientation = read(root / "orientation-results.json")
    before = read(root / "without-orientation" / "parsing-evaluation" / "report.json")
    after = read(root / "oriented" / "parsing-evaluation" / "report.json")
    runs = [path for path in (root / "gemini").iterdir() if (path / "gemini-run.json").is_file()]
    complete = [path for path in runs if len(read(path / "gemini-run.json")["calls"]) == len(orientation)
                and not read(path / "gemini-run.json")["unattempted_sources"]]
    if not complete:
        raise ValueError("No full-dataset Gemini comparison yet")
    ai_run = max(complete, key=lambda path: path.stat().st_mtime)
    comparison = read(ai_run / "comparison.json")
    metadata = read(ai_run / "gemini-run.json")
    stats = {"Bez orientacji": before["summary"], "Reguły + orientacja": after["summary"],
             "Gemini + orientacja": comparison["gemini"]}
    fields = ["store", "date", "total", "amount_due", "items.quantity", "items.unit_price", "items.before_discount",
              "items.discount", "items.total", "deposits.total", "deposit_returns.total"]
    rows = []
    for field in fields:
        for label, summary in stats.items():
            counts = summary["fields"].get(field, {})
            denominator = sum(value for key, value in counts.items() if key != "not_annotated")
            rows.append({"variant": label, "field": field, "match": counts.get("match", 0),
                         "evaluated": denominator, "missing": counts.get("missing", 0),
                         "different": counts.get("different", 0)})
    with (root / "presentation-metrics.csv").open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    changed = [case for case in orientation if case.get("changed_pages")]
    uncertain = [case for case in orientation if any(warning["code"] == "orientation_uncertain"
                                                    for warning in case.get("orientation_warnings", []))]
    orientation_times = [step["seconds"] for case in orientation for step in case.get("preparation_steps", [])
                         if step["name"] == "orient"]
    a_cases = {case["source"]: case for case in after["cases"]}
    b_cases = {case["source"]: case for case in before["cases"]}
    deltas = [{"source": name, "before": numeric_score(b_cases[name]), "after": numeric_score(case)}
              for name, case in a_cases.items() if name in b_cases]
    all_calls = [call for path in runs for call in read(path / "gemini-run.json")["calls"]]
    all_calls.extend(call for path in root.glob("api-prefill-*.json") for call in read(path))
    paid = [call for call in all_calls if not call["cache_hit"] and call["estimated_cost_usd"] is not None]
    fresh_sources = {call["source"] for call in paid if call.get("usage")}
    estimated_cost = sum(float(call["estimated_cost_usd"]) for call in paid)
    api_times = [call["seconds"] for call in paid if call.get("seconds") is not None]
    rule_times = [step["seconds"] for folder in (root / "oriented").iterdir()
                  if (folder / "result.json").is_file()
                  for step in read(folder / "result.json")["steps"] if step["name"] == "parse"]
    lines = ["# Świeży eksperyment: orientacja i porównanie parserów", "",
             f"Źródło OCR: `{root / 'oriented'}`.", f"Źródło porównania: `{ai_run}`.", "",
             f"Zbiór: {len(orientation)} dokumentów. Model Gemini: {metadata['model']}.",
             "Oba parsery otrzymują identyczny zapis OCR RapidOCR po orientacji. Gemini nie dostaje zdjęć ani wzorców.",
             "Wzorce służą wyłącznie ocenie; statusy i niepewne pola są zachowane w raportach.", "",
             "## Wpływ orientacji", "",
             f"Obrócone dokumenty: {len(changed)}; obrócone strony: {sum(len(case['changed_pages']) for case in changed)}.",
             f"Dokumenty z niepewną orientacją: {len(uncertain)}.",
             f"Mediana kroku orientacji: {median(orientation_times):.4f} s (pierwszy dokument obejmuje inicjalizację).",
             "Dla zmienionych obrazów wykonano świeży OCR także przed orientacją. "
             "Dla identycznych bajtów obrazu wspólne OCR i wynik reguł wykorzystano w obu wariantach.", "",
             "| Pole | Bez orientacji | Reguły + orientacja | Gemini + orientacja |",
             "|---|---:|---:|---:|"]
    for field in fields:
        values = [f"{row['match']}/{row['evaluated']}" for row in rows if row["field"] == field]
        lines.append(f"| {field} | {' | '.join(values)} |")
    lines.extend(["", "Licznik = dokładnie zgodne wartości, mianownik = pola z adnotacją, także brakujące. "
                  "Nie są to procenty poprawnych całych paragonów.", "", "## Produkty", "",
                  "| Wariant | Oczekiwane | W wyniku | Dopasowane | Brakujące | Nadmiarowe |",
                  "|---|---:|---:|---:|---:|---:|"])
    for label, summary in stats.items():
        group = summary["groups"]["items"]
        lines.append(f"| {label} | " + " | ".join(str(group.get(key, 0)) for key in
                     ("expected_count", "observed_count", "matched_count", "missing_count", "excess_count")) + " |")
    lines.extend(["", "## Wykonanie i koszt", "",
                  f"Błędy końcowego wykonania Gemini: {sum(not call['ok'] for call in metadata['calls'])}.",
                  f"Odpowiedzi z cache w końcowym przebiegu: {sum(call['cache_hit'] for call in metadata['calls'])}.",
                  f"Unikalne dokumenty z nowym wywołaniem API w trakcie tego eksperymentu: {len(fresh_sources)}; "
                  f"pozostałe odpowiedzi pochodziły z wcześniejszego cache: {len(orientation) - len(fresh_sources)}.",
                  f"Szacowany łączny koszt wywołań z raportowanym zużyciem w tym eksperymencie: ${estimated_cost:.6f}.",
                  "Koszt opiera się na zapisanych tokenach i stawkach eksperymentu, nie na rachunku dostawcy. "
                  "Próby bez raportowanego zużycia nie są wycenione; istniejący cache sprzed eksperymentu nie jest tu naliczany.",
                  f"Mediana lokalnego kroku parsera regułowego: {median(rule_times):.4f} s."])
    if api_times:
        lines.append(f"Mediana prób API z raportowanym zużyciem (bez cache): {median(api_times):.3f} s.")
    lines.extend(["", "## Zmienione przypadki", ""])
    for case in sorted(deltas, key=lambda item: abs(item["after"] - item["before"]), reverse=True):
        if case["before"] != case["after"]:
            lines.append(f"- `{case['source']}`: zgodne pola liczbowe produktów {case['before']} → {case['after']}.")
    for case in changed:
        name = case["source"]
        if name not in a_cases or name not in b_cases:
            continue
        for field, check in a_cases[name]["document_fields"].items():
            old = b_cases[name]["document_fields"][field]
            if old["status"] != check["status"]:
                lines.append(f"- `{name}`, `{field}`: {old['status']} → {check['status']}.")
    if not changed:
        lines.append("Krok nie obrócił żadnego dokumentu w tym zbiorze. "
                     "Nie ma podstaw do twierdzenia, że poprawił tu jakość; to pomiar jego kosztu i zachowania.")
    lines.extend(["", "## Ograniczenia", "",
                  "Jedna realizacja modelu i pojedyncze pomiary, bez strojenia promptu po wynikach. "
                  "Dopasowanie produktów jest heurystyczne. Zbiór był wcześniej używany do rozwoju reguł, "
                  "więc wynik nie jest niezależnym testem generalizacji parsera.",
                  "Nie należy utożsamiać poprawnej sumy, poprawnego JSON ani ukończenia API z pełnym odczytem produktów."])
    (root / "presentation-report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    (root / "presentation-summary.json").write_text(json.dumps({
        "orientation_documents": len(changed), "uncertain_documents": len(uncertain),
        "stats": stats, "deltas": deltas, "gemini_run": str(ai_run),
        "estimated_cost_usd": estimated_cost,
        "reference_statuses": dict(Counter(case["review_status"] for case in after["cases"]))},
        ensure_ascii=False, indent=2), encoding="utf-8")
    print(root / "presentation-report.md")


if __name__ == "__main__":
    main(sys.argv[1])
