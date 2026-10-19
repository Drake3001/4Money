"""Reference-guided OCR audit, not a receipt parser or OCR accuracy benchmark."""

import json
import re
import unicodedata
from collections import Counter
from decimal import Decimal
from difflib import SequenceMatcher
from math import hypot
from pathlib import Path

from extraction.contracts import OCRResult
from extraction.reporting import to_dict

NAME_THRESHOLD = 0.65
NUMBER = r"\d+(?:[.,]\d+)?"
PURCHASE_PREFIX = re.compile(
    rf"(?<![\w.,]){NUMBER}(?:\s*(?:szt\.?|kg|g|l))?\s*[x×*]\s*{NUMBER}(?![\d.,])",
    re.IGNORECASE,
)
PURCHASE = re.compile(
    rf"(?<![\w.,])({NUMBER})(?:\s*(?:szt\.?|kg|g|l))?\s*[x×*]\s*({NUMBER})\s+({NUMBER})(?![\d.,])",
    re.IGNORECASE,
)
MONEY = re.compile(r"(?<![\w.,])-?\s*\d+[.,]\d{2}(?!\d)")
NEGATIVE = re.compile(r"(?<![\w.,])-\s*\d+[.,]\d{2}(?!\d)")


def normalize_name(text: str) -> str:
    # Case/spacing/punctuation are ignored; Polish letters remain significant.
    return "".join(
        char
        for char in unicodedata.normalize("NFKC", text).casefold()
        if char.isalnum()
    )


def _decimal(text: str) -> Decimal:
    return Decimal(text.replace(",", ".").replace(" ", ""))


def _lines(raw: dict) -> list[dict]:
    lines = []
    for page in raw["pages"]:
        for index, line in enumerate(page["lines"]):
            box = line["box"]
            lines.append(
                {
                    **line,
                    "page": page["page_number"],
                    "index": index,
                    "x": sum(point[0] for point in box) / 4,
                    "y": sum(point[1] for point in box) / 4,
                    "height": max(
                        1,
                        (
                            hypot(box[3][0] - box[0][0], box[3][1] - box[0][1])
                            + hypot(box[2][0] - box[1][0], box[2][1] - box[1][1])
                        )
                        / 2,
                    ),
                    "slope": max(
                        -0.5,
                        min(0.5, (box[1][1] - box[0][1]) / (box[1][0] - box[0][0])),
                    )
                    if box[1][0] != box[0][0]
                    else 0,
                }
            )
    return sorted(lines, key=lambda line: (line["page"], line["y"], line["x"]))


def _name_score(item: dict, line: dict) -> float:
    text = line["text"]
    if re.match(r"\s*opust\b", text, re.IGNORECASE):
        return 0  # A name on a discount line is not a recognized purchase line.
    expected = normalize_name(item["name"])
    purchase = PURCHASE_PREFIX.search(text)
    name_texts = [text]
    if purchase:
        name_texts.append(text[:purchase.start()].rstrip())
    candidates = [normalize_name(value) for value in name_texts]
    marker = item.get("printed_marker")
    for value in name_texts:
        if marker and re.search(rf"\s+{re.escape(marker)}\s*$", value, re.IGNORECASE):
            candidates.append(normalize_name(re.sub(rf"\s+{re.escape(marker)}\s*$", "", value, flags=re.IGNORECASE)))
    tax_code = item.get("tax_code")
    if tax_code:
        for value in name_texts:
            candidates.append(normalize_name(re.sub(
                rf"\s+{re.escape(tax_code)}\s*$", "", value, flags=re.IGNORECASE
            )))
    return max(
        SequenceMatcher(None, expected, candidate, autojunk=False).ratio()
        for candidate in candidates
    )


def _anchors(items: list[dict], lines: list[dict]) -> dict[int, dict]:
    # Sequence alignment: each OCR fragment can identify at most one product.
    # Gaps allow dropped names and unrelated header/background fragments.
    scores = [[_name_score(item, line) for line in lines] for item in items]
    rows, columns = len(items), len(lines)
    dp = [[0.0] * (columns + 1) for _ in range(rows + 1)]
    choices = {}
    for i in range(1, rows + 1):
        for j in range(1, columns + 1):
            options = [(dp[i - 1][j], "skip_item"), (dp[i][j - 1], "skip_line")]
            score = scores[i - 1][j - 1]
            if score >= NAME_THRESHOLD:
                options.append((dp[i - 1][j - 1] + score, "match"))
            dp[i][j], choices[i, j] = max(options, key=lambda option: option[0])
    anchors = {}
    i, j = rows, columns
    while i and j:
        choice = choices[i, j]
        if choice == "match":
            anchors[i - 1] = {**lines[j - 1], "similarity": scores[i - 1][j - 1]}
            i, j = i - 1, j - 1
        elif choice == "skip_item":
            i -= 1
        else:
            j -= 1
    return anchors


def _check(expected: str, observed: list[Decimal]) -> dict:
    values = sorted(set(observed))
    if not values:
        status = "missing"
    elif len(values) > 1:
        status = "ambiguous"
    elif values[0] == Decimal(expected):
        status = "match"
    else:
        status = "different"
    return {
        "expected": expected,
        "observed": [str(value) for value in values],
        "status": status,
    }


def _purchase_fragments(index, anchors, lines):
    """Assign each fragment once by geometry, without using expected prices."""
    if index not in anchors:
        return []
    fragments = []
    for line in lines:
        distances = []
        for owner, candidate in anchors.items():
            if line["page"] != candidate["page"]:
                continue
            dy = line["y"] - candidate["slope"] * (line["x"] - candidate["x"]) - candidate["y"]
            distances.append((abs(dy) / max(candidate["height"], line["height"]), owner))
        if distances:
            distance, owner = min(distances)
            if owner == index and distance <= 0.85:
                fragments.append(line)
    return sorted(fragments, key=lambda line: line["x"])


def _merchant(reference, lines, anchors):
    expected = reference.get("store")
    if not expected:
        return {"expected": None, "observed": None, "status": "not_available"}
    header = []
    first_anchor = min((a["y"] for a in anchors.values() if a["page"] == 1), default=float("inf"))
    for line in lines:
        if line["page"] != 1 or line["y"] >= first_anchor:
            continue
        if normalize_name(line["text"]).startswith("paragonfiskalny"):
            break
        header.append(line)
    target = normalize_name(expected)
    candidates = []
    length = len(re.findall(r"[\w]+", expected))
    for line in header:
        words = re.findall(r"[\w]+", line["text"], flags=re.UNICODE)
        for start in range(len(words) - length + 1):
            word = " ".join(words[start:start + length])
            score = SequenceMatcher(None, target, normalize_name(word), autojunk=False).ratio()
            candidates.append((score, word, line))
    if not candidates or max(c[0] for c in candidates) < 0.75:
        return {"expected": expected, "observed": None, "status": "missing", "scope": "header"}
    score, word, line = max(candidates, key=lambda candidate: candidate[0])
    return {"expected": expected, "observed": word,
            "status": "match" if score == 1 else "approximate", "similarity": round(score, 4),
            "scope": "header", "evidence": {"text": line["text"], "box": line["box"], "page": line["page"]}}


def audit_receipt(reference: dict, raw: OCRResult | dict) -> dict:
    if isinstance(raw, OCRResult):
        raw = to_dict(raw)
    lines = _lines(raw)
    anchors = _anchors(reference["items"], lines)
    items = []
    for index, item in enumerate(reference["items"]):
        anchor = anchors.get(index)
        entry = {
            "number": index + 1,
            "expected_name": item["name"],
            "observed_name": anchor["text"] if anchor else None,
            "name_status": "missing"
            if anchor is None
            else "match"
            if anchor["similarity"] == 1
            else "approximate",
            "name_similarity": round(anchor["similarity"], 4) if anchor else None,
            "confidence": anchor["confidence"] if anchor else None,
            "fields": {},
        }
        window = []
        purchase_lines = []
        if anchor:

            def projected_y(line):
                return line["y"] - anchor["slope"] * (line["x"] - anchor["x"])

            following = next(
                (
                    anchors[k]
                    for k in range(index + 1, len(reference["items"]))
                    if k in anchors
                ),
                None,
            )
            lower = anchor["y"] - 0.5 * anchor["height"]
            upper = (
                projected_y(following) - 0.2 * following["height"]
                if following and following["page"] == anchor["page"]
                else float("inf")
            )
            summaries = [
                projected_y(line) - 0.5 * line["height"]
                for line in lines
                if line["page"] == anchor["page"]
                and line["y"] > anchor["y"]
                and normalize_name(line["text"]).startswith(
                    ("podsum", "podsun", "sprzed")
                )
            ]
            if summaries:
                upper = min(upper, min(summaries))
            window = [
                line
                for line in lines
                if line["page"] == anchor["page"] and lower <= projected_y(line) < upper
            ]
            purchase_lines = [
                line
                for line in window
                if abs(projected_y(line) - anchor["y"])
                <= 0.5 * max(anchor["height"], line["height"])
            ]
        purchase_lines = _purchase_fragments(index, anchors, lines)
        purchase_text = " ".join(line["text"] for line in purchase_lines)
        patterns = [
            tuple(_decimal(value) for value in match.groups())
            for match in PURCHASE.finditer(purchase_text)
        ]
        for field, position in (
            ("quantity", 0),
            ("unit_price", 1),
            ("before_discount", 2),
        ):
            entry["fields"][field] = _check(
                item[field], [pattern[position] for pattern in patterns]
            )
        discounts = [
            _decimal(match.group())
            for line in window
            for match in NEGATIVE.finditer(line["text"].replace("−", "-"))
        ]
        if Decimal(item["discount"]) != 0:
            entry["fields"]["discount"] = _check(item["discount"], discounts)
            # Discounted final amounts are separate printed fragments; exclude
            # quantity × price expressions to avoid using gross amounts.
            totals = [
                _decimal(match.group())
                for line in window
                if re.fullmatch(
                    r"\s*\d+[.,]\d{2}\s*[A-Z]?\s*", line["text"], re.IGNORECASE
                )
                and projected_y(line) > anchor["y"] + 0.5 * anchor["height"]
                for match in MONEY.finditer(line["text"])
                if _decimal(match.group()) >= 0
            ]
            if item.get("total_is_printed", True):
                entry["fields"]["total"] = _check(item["total"], totals)
            else:
                entry["fields"]["total"] = {"expected": item["total"], "observed": [], "status": "not_printed"}
                gross = entry["fields"]["before_discount"]
                discount = entry["fields"]["discount"]
                values = [Decimal(gross["observed"][0]) + Decimal(discount["observed"][0])] if (
                    len(gross["observed"]) == len(discount["observed"]) == 1
                ) else []
                entry["computed_total"] = {**_check(item["total"], values), "basis": "observed_gross_plus_discount"}
        else:
            # No printed discount to score. The third purchase number is total.
            entry["fields"]["total"] = (
                _check(item["total"], [pattern[2] for pattern in patterns])
                if item.get("total_is_printed", True)
                else {"expected": item["total"], "observed": [], "status": "not_printed"}
            )
        entry["evidence"] = [
            {"text": line["text"], "page": line["page"], "box": line["box"]}
            for line in window
        ]
        entry["purchase_evidence"] = [{"text": line["text"], "page": line["page"], "box": line["box"]} for line in purchase_lines]
        entry["reference_uncertainties"] = [
            uncertain
            for uncertain in reference.get("uncertain_fields", [])
            if uncertain["field"].startswith(f"items[{index}].")
        ]
        items.append(entry)
    all_text = "\n".join(line["text"] for line in lines)
    # Deliberately only existence checks; these do not assign sums to labels.
    numbers = {_decimal(match.group()) for match in MONEY.finditer(all_text)}
    amounts = {
        field: {
            "expected": reference[field],
            "present_in_document": Decimal(reference[field]) in numbers,
        }
        for field in ("total", "amount_due")
    }
    dates = {
        field: {
            "expected": reference[field],
            "present_in_document": reference[field] in all_text,
        }
        for field in ("date", "time")
    }
    counters = Counter(entry["name_status"] for entry in items)
    fields = Counter(
        check["status"] for entry in items for check in entry["fields"].values()
        if check["status"] != "not_printed"
    )
    return {
        "source": reference["source"],
        "method": raw.get("engine"),
        "version": raw.get("version"),
        "settings": raw.get("settings"),
        "expected_item_count": len(items),
        "name_counts": dict(counters),
        "numeric_field_counts": dict(fields),
        "derived_field_counts": dict(Counter(item["computed_total"]["status"] for item in items if "computed_total" in item)),
        "merchant": _merchant(reference, lines, anchors),
        "document_values": {**amounts, **dates},
        "items": items,
    }


def _cell(value: object) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def _markdown(report: dict) -> str:
    output = [
        "# Porównanie OCR ze wzorcami",
        "",
        "To audyt wspomagany wzorcem, nie wynik parsera ani procent dokładności OCR.",
        "Nazwy ignorują wielkość liter, odstępy i interpunkcję; zachowują polskie znaki.",
        "Dopasowanie przybliżone wskazuje kandydata (próg 0,65), a nie poprawny odczyt.",
        "Liczby sprawdzamy w obszarze przy dopasowanym produkcie. Kilka różnych wartości oznacza niejednoznaczność.",
        "Fragmenty wiersza zakupowego łączymy według położenia; obsługujemy x, × i *. Kod podatkowy na końcu nazwy oddzielamy tylko zgodnie ze wzorcem.",
        "Kwoty oznaczone jako niewydrukowane oceniamy osobno przez obliczenie z odczytanych danych; nie wliczamy ich do odczytu liczb.",
        "Nie zgadujemy cyfr, nie poprawiamy nazw i nie liczymy CER/WER całego zdjęcia.",
        "",
        "| Paragon | Sklep | Pozycje wzorca | Nazwy zgodne | Przybliżone | Brak nazwy | Liczby zgodne | Inne / brak / niejednoznaczne | Obliczone zgodne |",
        "|---|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for case in report["cases"]:
        names, fields = case["name_counts"], case["numeric_field_counts"]
        output.append(
            f"| {_cell(case['source'])} | {case['merchant']['status']} | {case['expected_item_count']} | {names.get('match', 0)} | {names.get('approximate', 0)} | {names.get('missing', 0)} | {fields.get('match', 0)} | {sum(fields.values()) - fields.get('match', 0)} | {case['derived_field_counts'].get('match', 0)} |"
        )
    for case in report["cases"]:
        output.extend(
            [
                "",
                f"## {case['source']}",
                "",
                f"Metoda: `{case['method']}`, wersja: `{case['version']}`.",
                "",
                "Wystąpienie wartości w tekście (nie potwierdza przypisania do właściwej etykiety):",
            ]
        )
        for field, value in case["document_values"].items():
            output.append(
                f"- {field}: `{value['expected']}` — {'znaleziono' if value['present_in_document'] else 'brak'}."
            )
        merchant = case["merchant"]
        output.extend(["", f"Sklep (marka w nagłówku): `{_cell(merchant['expected'])}` ← `{_cell(merchant['observed'] or 'brak')}` — {merchant['status']}.",
                       "Brak dopasowania marki w nagłówku nie dowodzi błędu OCR: marka może nie być wydrukowana."])
        output.extend(
            [
                "",
                "| # | Nazwa oczekiwana | Fragment OCR | Nazwa | Ilość | Cena jedn. | Przed rabatem | Rabat | Końcowa kwota wydrukowana | Obliczona kwota |",
                "|---:|---|---|---|---|---|---|---|---|---|",
            ]
        )
        for item in case["items"]:
            checks = []
            for field in (
                "quantity",
                "unit_price",
                "before_discount",
                "discount",
                "total",
            ):
                check = item["fields"].get(field)
                checks.append(
                    "—"
                    if check is None
                    else _cell(
                        f"{check['status']}: {check['expected']} ← {', '.join(check['observed']) or 'brak'}"
                    )
                )
            output.append(
                f"| {item['number']} | {_cell(item['expected_name'])} | {_cell(item['observed_name'] or 'brak')} | {item['name_status']} | "
                + " | ".join(checks)
                + " | " + (_cell(f"{item['computed_total']['status']}: {item['computed_total']['expected']} ← {', '.join(item['computed_total']['observed']) or 'brak danych'}") if "computed_total" in item else "—")
                + " |"
            )
        uncertain = [
            entry for item in case["items"] for entry in item["reference_uncertainties"]
        ]
        if uncertain:
            output.extend(
                ["", "Niepewności wzorca:"]
                + [f"- {entry['field']}: {entry['detail']}" for entry in uncertain]
            )
    output.extend(
        [
            "",
            "## Jak interpretować",
            "",
            "`match`: zgodne po opisanej normalizacji; `approximate`: podobny fragment do ręcznej kontroli;",
            "`missing`: audyt nie przypisał wartości do pozycji (nie dowodzi braku w OCR); `different`: jedna odczytana wartość różni się od wzorca;",
            "`ambiguous`: kilka różnych wartości w obszarze — brak pewnego przypisania.",
            "`not_printed`: kwota niewydrukowana według adnotacji; jej obliczenie jest osobną kontrolą, nie odczytem OCR.",
            "",
            "Okna przestrzenne i dopasowanie nazw są heurystyką. Przechylone kolumny, brak nazwy lub brak granicy",
            "między ostatnim produktem a podsumowaniem mogą mieszać liczby. Szczegóły i współrzędne są w report.json.",
            "Powtarzających się kwot nie uznajemy za poprawnie przypisane tylko dlatego, że występują w dokumencie.",
            "",
        ]
    )
    if report["skipped"]:
        output.extend(
            ["## Pominięte przypadki", ""]
            + [f"- {case['reference']}: {case['reason']}" for case in report["skipped"]]
        )
    return "\n".join(output) + "\n"


def write_evaluation(run_dir: Path, references_dir: Path, results=None) -> Path | None:
    references = sorted(references_dir.glob("*.expected.json"))
    if not references:
        return None
    report = {
        "schema_version": 2,
        "audit_settings": {"purchase_max_distance_in_heights": 0.85, "merchant_threshold": 0.75, "merchant_scope": "header"},
        "run": run_dir.name,
        "name_threshold": NAME_THRESHOLD,
        "cases": [],
        "skipped": [],
    }
    for path in references:
        try:
            reference = json.loads(path.read_text(encoding="utf-8"))
            source = reference["source"]
            if Path(source).name != source or source in (".", ".."):
                raise ValueError("Reference source must be a filename or receipt folder name")
            if (references_dir / source).is_dir():
                from extraction.runner import MIME_TYPES

                sources = sorted(
                    p.name for p in (references_dir / source).iterdir()
                    if p.is_file() and p.suffix.lower() in MIME_TYPES
                )
                if reference.get("sources") != sources:
                    raise ValueError("Reference sources must match the receipt folder's ordered files")
            raw_path = run_dir / source / "result.json"
            if results is not None and source in results:
                raw = results[source].output
                if not isinstance(raw, OCRResult):
                    raise ValueError("No successful OCR output")
                report["cases"].append(audit_receipt(reference, raw))
                continue
            if not raw_path.is_file():
                report["skipped"].append(
                    {"reference": path.name, "reason": "Brak wyniku OCR dla zdjęcia"}
                )
                continue
            raw = json.loads(raw_path.read_text(encoding="utf-8"))["output"]
            if not isinstance(raw, dict) or raw.get("kind") != "ocr":
                raise ValueError("No successful OCR output")
            report["cases"].append(audit_receipt(reference, raw))
        except (OSError, ValueError, KeyError, TypeError, ArithmeticError) as exc:
            report["skipped"].append({"reference": path.name, "reason": str(exc)})
    output_dir = run_dir / "evaluation"
    output_dir.mkdir(exist_ok=False)
    (output_dir / "report.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    (output_dir / "report.md").write_text(_markdown(report), encoding="utf-8")
    return output_dir / "report.md"
