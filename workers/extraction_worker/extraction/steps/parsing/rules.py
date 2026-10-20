"""Polish receipt rules. References and filenames never participate in parsing."""

import re
import unicodedata
from datetime import date
from decimal import Decimal, ROUND_HALF_UP

from ...contracts import (
    ExtractionWarning, FieldEvidence, OCRResult, ReceiptData, ReceiptDeposit,
    ReceiptItem, StepResult,
)
from .geometry import Row, center, height, receipt_rows, y_at

NUMBER = r"\d+(?:[.,]\d+)?"
AMOUNT = r"-?\s*\d+[.,]\d{2}(?![\d.,])"
MONEY = re.compile(rf"(?<![\w.,]){AMOUNT}")
PURCHASE = re.compile(
    rf"(?<![\w.,])(?P<quantity>{NUMBER})(?:\s*(?P<unit>szt\.?|kg|g|l))?"
    rf"\s*[x×*]\s*(?P<price>{AMOUNT})", re.IGNORECASE,
)
PRICE = re.compile(rf"(?<!\w)[x×*]\s*(?P<price>{AMOUNT})", re.IGNORECASE)
AMOUNT_ONLY = re.compile(rf"\s*({AMOUNT})\s*[A-G]?\s*", re.IGNORECASE)
DATE = re.compile(r"(?<!\d)(\d{4})[-./](\d{2})[-./](\d{2})(?!\d)|"
                  r"(?<!\d)(\d{2})[-./](\d{2})[-./](\d{4})(?!\d)")


def normalized(text):
    text = text.casefold().replace("ł", "l")
    return " ".join("".join(c for c in unicodedata.normalize("NFKD", text)
                            if not unicodedata.combining(c)).split())


def decimal(text):
    return Decimal(text.replace(",", ".").replace(" ", ""))


def amounts(text):
    return [decimal(match.group()) for match in MONEY.finditer(text.replace("−", "-"))]


def clean_name(text):
    text = re.sub(r"^\s*opust\b\s*", "", text, flags=re.IGNORECASE)
    # Standalone tax/printed markers are not part of a description. Attached
    # suffixes (e.g. COCA 330ml-A) are retained as printed.
    text = re.sub(r"\s+[A-GX]\s*$", "", text, flags=re.IGNORECASE)
    return " ".join(text.split()) or None


def section(text):
    key = normalized(text)
    if "paragon fiskalny" in key:
        return "items"
    if key.startswith("opakowania"):
        if "wydania" in key:
            return "deposits"
        if "przyjecia" in key:
            return "deposit_returns"
        return "summary"
    if key.startswith(("do zap", "rozliczenie", "niefiskalny")):
        return "finished" if key.startswith(("rozliczenie", "niefiskalny")) else "summary"
    if key.startswith(("podsum", "podsun", "sprzed", "suma", "opusty lacznie", "ptu", "kwota ptu")):
        return "summary"
    return None


def purchase(row: Row) -> ReceiptItem | None:
    text = row.text.replace("−", "-")
    match = PURCHASE.search(text)
    fallback = False
    if match is None:
        if normalized(text).startswith("opust"):
            return None
        match = PRICE.search(text)
        fallback = True
    if match is None:
        return None
    prefix = text[:match.start()]
    if fallback:
        prefix = re.sub(r"\s+\S*[.,]\d+\s*$", "", prefix)
    tail = amounts(text[match.end():])
    if normalized(text).startswith("opust") and tail and tail[0] < 0:
        return None
    return ReceiptItem(
        name=clean_name(prefix),
        quantity=None if fallback else decimal(match["quantity"]),
        unit=None if fallback or not match["unit"] else match["unit"].casefold().rstrip("."),
        unit_price=decimal(match["price"]),
        before_discount=tail[0] if tail else None,
    )


class ReceiptParser:
    def __init__(self, ocr: OCRResult):
        self.ocr = ocr
        self.rows = receipt_rows(ocr)
        self.data = ReceiptData()
        self.warnings = []
        self.used_names = set()
        self.ambiguous_fields = set()
        self.saw_amount_due = False

    def warn(self, code, message, field=None):
        self.warnings.append(ExtractionWarning(code, message, field))

    def record(self, path, rows, method="observed"):
        refs = tuple(dict.fromkeys(ref for row in rows for ref in row.refs))
        self.data.evidence[path] = FieldEvidence(refs, method)

    def unique(self, values, path):
        values = set(values)
        if len(values) > 1:
            self.ambiguous_fields.add(path)
            self.warn("ambiguous_field", "Conflicting values in the same receipt field", path)
            return None
        return next(iter(values), None)

    def header(self, start):
        # Only the header before the first fiscal marker is eligible as vendor.
        header = self.rows[:start]
        aliases = ((r"\bzabka\b", "Żabka"), (r"\blidl\b", "Lidl"),
                   (r"\baldi\b", "ALDI"), (r"\bmc\s*donald[’']?s\b", "McDonald's"))
        for row in header:
            for pattern, merchant in aliases:
                if re.search(pattern, normalized(row.text)):
                    self.data.store = merchant
                    self.record("store", [row])
                    break
            if self.data.store:
                break
        if self.data.store is None:
            candidates = [row for row in header if len(row.text) >= 4
                          and not re.search(r"\d", row.text)
                          and not re.search(r"\b(nip|bdo|adres|ul\b|paragon|e-paragon|pobierz)\b",
                                            normalized(row.text))]
            if candidates:
                self.data.store = candidates[0].text.strip()
                self.record("store", [candidates[0]])

    def document_fields(self, start):
        totals, dues, dates, currencies = [], [], [], []
        total_rows, due_rows, date_rows, currency_rows = [], [], [], []
        ended = False
        current_page = self.rows[start].page
        for index, row in enumerate(self.rows[start:]):
            if row.page != current_page:
                current_page, ended = row.page, False
            key = normalized(row.text)
            if index and "paragon fiskalny" in key and row.page == self.rows[start].page:
                break  # A second fiscal document is not a continuation of this one.
            if "niefiskalny" in key:
                ended = True
            # Footer dates belong to the transaction even after payments begin.
            if not ended or DATE.search(row.text):
                for match in DATE.finditer(row.text):
                    parts = match.groups()
                    try:
                        parsed = date(*map(int, parts[:3] if parts[0] else parts[3:][::-1]))
                    except ValueError:
                        self.warn("invalid_date", "Invalid date read by OCR", "date")
                        continue
                    dates.append(parsed)
                    date_rows.append(row)
            if key.startswith("rozliczenie"):
                ended = True
            if ended:
                continue
            if re.search(r"\b(pln|zl)\b", key):
                currencies.append("PLN")
                currency_rows.append(row)
            target = None
            if re.search(r"\bsuma\s+(?:pln|zl)\b", key):
                target = (totals, total_rows)
            elif key.startswith("do zap"):
                self.saw_amount_due = True
                target = (dues, due_rows)
            if target is not None:
                values = amounts(row.text)
                evidence = [row]
                absolute = start + index
                if not values and absolute + 1 < len(self.rows):
                    following = self.rows[absolute + 1]
                    fx = center(following.members[0][1])[0]
                    gap = abs(y_at(following.members[0][1], fx)
                              - max(y_at(fragment, fx) for _, fragment in row.members))
                    tolerance = 2 * max(height(fragment) for _, fragment in (*row.members, *following.members))
                    if following.page == row.page and gap <= tolerance and re.fullmatch(
                        rf"\s*{AMOUNT}\s*(?:PLN|zł)?\s*", following.text, re.IGNORECASE
                    ):
                        values = amounts(following.text)
                        evidence.append(following)
                target[0].extend(values)
                target[1].extend(evidence)
        for name, values, rows in (("total", totals, total_rows), ("amount_due", dues, due_rows),
                                   ("date", dates, date_rows), ("currency", currencies, currency_rows)):
            value = self.unique(values, name)
            setattr(self.data, name, value)
            if value is not None:
                self.record(name, rows)

    def item_block(self, rows, next_row=None):
        first = rows[0]
        item = purchase(first)
        if item is None:
            return
        index = len(self.data.items)
        path = f"items[{index}]"
        # Anchor numeric columns at their own baseline. A net price may have
        # been grouped into the next product row by the general reading view.
        numeric = next((fragment for _, fragment in first.members
                        if PURCHASE.search(fragment.text) or PRICE.search(fragment.text)), first.members[-1][1])
        nx, ny = center(numeric)
        numeric_match = PURCHASE.search(numeric.text) or PRICE.search(numeric.text)
        printed_totals = amounts(numeric.text[numeric_match.end():]) if numeric_match else []
        printed_totals.extend(
            decimal(m.group(1)) for _, fragment in first.members
            if fragment is not numeric and (m := AMOUNT_ONLY.fullmatch(fragment.text))
            and abs(y_at(fragment, nx) - ny) <= 0.45 * max(height(fragment), height(numeric))
        )
        ambiguous_gross = len(set(printed_totals)) > 1
        item.before_discount = self.unique(printed_totals, path + ".before_discount")
        name_rows = []
        first_index = self.rows.index(first)
        last_name_y = ny
        for previous in reversed(self.rows[:first_index]):
            if previous.page != first.page or any(ref in self.used_names for ref in previous.refs):
                break
            previous_y = max(y_at(fragment, nx) for _, fragment in previous.members)
            gap = last_name_y - previous_y
            if not 0 < gap <= 1.6 * height(numeric) or len(name_rows) >= 3:
                break
            if (section(previous.text) or purchase(previous) or amounts(previous.text)
                    or normalized(previous.text).startswith("opust")
                    or len(previous.text.strip()) < 3):
                break
            name_rows.insert(0, previous)
            last_name_y = previous_y
        if name_rows:
            item.name = clean_name(" ".join([row.text for row in name_rows] + [item.name or ""]))
        if item.name:
            self.used_names.update(first.refs)
            self.used_names.update(ref for row in name_rows for ref in row.refs)
        bottom = ny + 8 * height(numeric)
        if next_row is not None and next_row.page == first.page:
            next_numeric = next((fragment for _, fragment in next_row.members
                                 if PURCHASE.search(fragment.text) or PRICE.search(fragment.text)), next_row.members[-1][1])
            bottom = min(bottom, y_at(next_numeric, nx) - 0.45 * height(next_numeric))
        fragment_rows = []
        for page in self.ocr.pages:
            if page.page_number != first.page:
                continue
            for fi, fragment in enumerate(page.lines):
                fy = y_at(fragment, nx)
                if ny + 0.45 * height(numeric) < fy < bottom:
                    fragment_rows.append(Row(first.page, ((fi, fragment),)))
        # A summary label can share a reconstructed row with the last net price;
        # only amount-only fragments remain eligible here.
        discounts = [(row, value) for row in fragment_rows
                     for value in amounts(row.text) if value < 0]
        opust = [row for row in rows if normalized(row.text).startswith("opust")]
        has_discount = bool(discounts or opust)
        discount_rows = [row for row, _ in discounts]
        if has_discount:
            item.discount = sum((value for _, value in discounts), Decimal("0")) if discounts else None
            if item.discount is None:
                self.warn("missing_discount", "Discount is present but its amount is unreadable", path + ".discount")
        else:
            item.discount = Decimal("0.00")
        totals = [(row, decimal(m.group(1))) for row in fragment_rows
                  if (m := AMOUNT_ONLY.fullmatch(row.text)) and decimal(m.group(1)) >= 0]
        if totals:
            item.total = self.unique([value for _, value in totals], path + ".total")
            if item.total is not None:
                self.record(path + ".total", [row for row, _ in totals])
                if item.before_discount is not None and (item.discount is None or not has_discount):
                    difference = item.total - item.before_discount
                    if difference <= 0:
                        item.discount = difference
                        self.record(path + ".discount", [first, *[row for row, _ in totals]], "derived")
                    else:
                        item.discount = None
                        self.warn("ambiguous_field", "Final amount exceeds the amount before discount", path + ".total")
        elif item.before_discount is not None and item.discount is not None:
            item.total = item.before_discount + item.discount
            self.record(path + ".total", [first, *discount_rows],
                        "derived" if has_discount else "observed")
        if (item.before_discount is None and not ambiguous_gross
                and item.quantity is not None and item.unit_price is not None):
            item.before_discount = (item.quantity * item.unit_price).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
            self.record(path + ".before_discount", [first], "derived")
            if item.total is None and item.discount is not None:
                item.total = item.before_discount + item.discount
                self.record(path + ".total", [first, *discount_rows], "derived")
        for field in ("name", "quantity", "unit", "unit_price", "before_discount"):
            if getattr(item, field) is not None and path + "." + field not in self.data.evidence:
                self.record(path + "." + field, [*name_rows, first] if field == "name" else [first])
        if path + ".discount" not in self.data.evidence:
            self.record(path + ".discount", discount_rows or [first], "observed" if has_discount else "derived")
        if item.name is None:
            self.warn("missing_item_name", "Purchase amounts have no readable product name", path + ".name")
        if item.quantity is None:
            self.warn("missing_quantity", "Purchase quantity is unreadable", path + ".quantity")
        if opust and normalized(first.text).startswith("opust") and item.name:
            self.warn("name_from_discount", "Product name was read from its discount label", path + ".name")
        self.data.items.append(item)

    def deposit_block(self, row, kind, pending):
        parsed = purchase(row)
        evidence = [row]
        if parsed:
            item = parsed
            entry = ReceiptDeposit(item.name, item.quantity, item.unit_price, item.before_discount)
        else:
            values = amounts(row.text)
            if not values:
                return row
            name = clean_name(MONEY.sub("", row.text))
            if pending is not None:
                name = pending.text.strip()
                evidence.insert(0, pending)
            entry = ReceiptDeposit(name=name, total=self.unique(values, kind))
        if entry.name is None and pending is not None:
            entry.name = pending.text.strip()
            evidence.insert(0, pending)
        entries = getattr(self.data, kind)
        path = f"{kind}[{len(entries)}]"
        for field in ("name", "quantity", "unit_price", "total"):
            if getattr(entry, field) is not None:
                self.record(path + "." + field, evidence)
        entries.append(entry)
        return None

    def run(self):
        start = next((i for i, row in enumerate(self.rows)
                      if "paragon fiskalny" in normalized(row.text)), None)
        if start is None:
            self.warn("missing_receipt_section", "No fiscal receipt marker found")
            return StepResult(self.data, tuple(self.warnings))
        self.header(start)
        self.document_fields(start)
        state, block, pending = "items", [], None
        current_page = self.rows[start].page
        saw_deposits = False
        for row in self.rows[start + 1:]:
            if row.page != current_page:
                if block:
                    self.item_block(block)
                    block = []
                current_page = row.page
                if any(candidate.page == row.page and section(candidate.text) == "items"
                       for candidate in self.rows):
                    state = "header"
                if pending is not None:
                    self.warn("unparsed_deposit", "Deposit label has no readable amount on its page", state)
                pending = None
            new_state = section(row.text)
            if new_state is not None:
                if block:
                    self.item_block(block, row)
                    block = []
                if pending is not None:
                    self.warn("unparsed_deposit", "Deposit label has no readable amount", state)
                pending = None
                if new_state == "items":
                    if state != "header":
                        break
                state = new_state
                saw_deposits |= state in ("deposits", "deposit_returns")
                continue
            if state == "items":
                if purchase(row):
                    if block:
                        self.item_block(block, row)
                    block = [row]
                elif block:
                    block.append(row)
                elif normalized(row.text).startswith("opust") or any(value < 0 for value in amounts(row.text)):
                    self.warn("unassigned_discount", "Discount has no purchase block on this page", "items")
            elif state in ("deposits", "deposit_returns"):
                pending = self.deposit_block(row, state, pending)
        if block:
            self.item_block(block)
        if pending is not None:
            self.warn("unparsed_deposit", "Deposit label has no readable amount", state)
        if (self.data.amount_due is None and self.data.total is not None
                and not saw_deposits and not self.saw_amount_due):
            self.data.amount_due = self.data.total
            self.data.evidence["amount_due"] = FieldEvidence(self.data.evidence["total"].fragments, "derived")
        elif self.data.amount_due is None and "amount_due" not in self.ambiguous_fields:
            self.warn("missing_amount_due", "Amount due could not be established", "amount_due")
        return StepResult(self.data, tuple(self.warnings))


def parse_receipt(ocr: OCRResult) -> StepResult:
    return ReceiptParser(ocr).run()
