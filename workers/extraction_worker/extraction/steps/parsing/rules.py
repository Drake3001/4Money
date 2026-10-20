"""Polish receipt rules. References and filenames never participate in parsing."""

import re
import unicodedata
from datetime import date
from decimal import Decimal, ROUND_HALF_UP
from difflib import SequenceMatcher

from ...contracts import (
    ExtractionWarning, FieldEvidence, OCRResult, ReceiptData, ReceiptDeposit,
    ReceiptItem, StepResult,
)
from .geometry import Row, center, height, receipt_rows, upright_ocr, y_at

NUMBER = r"\d+(?:[.,]\d+)?"
AMOUNT_VALUE = r"-?\s*\d+[.,]\s*\d{2}"
AMOUNT = AMOUNT_VALUE + r"(?![\d.,])"
MONEY = re.compile(rf"(?<![\w.,]){AMOUNT}")
PURCHASE = re.compile(
    rf"(?<![\w.,])(?P<quantity>{NUMBER})(?:\s*(?P<unit>szt\.?|kg|g|ltr|l))?"
    rf"\s*[x×*�]\s*(?P<price>{AMOUNT})", re.IGNORECASE,
)
PRICE = re.compile(rf"(?<![a-z])[x×*�]\s*(?P<price>{AMOUNT})", re.IGNORECASE)
AMOUNT_ONLY = re.compile(rf"\s*(?:[A-GX]\s+)?({AMOUNT_VALUE})0*\s*[A-G�]?\s*", re.IGNORECASE)
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


def fiscal_marker(text):
    words = re.findall(r"[a-z]+", normalized(text))
    return any(SequenceMatcher(None, "".join(words[i:i+n]), "paragonfiskalny").ratio() >= 0.86
               for i in range(len(words)) for n in (2, 3))


def discount_marker(text):
    words = re.findall(r"[a-z]+", normalized(text))
    return bool(words) and words[0] not in ("opusty", "rabaty") and any(
        SequenceMatcher(None, words[0], label).ratio() >= 0.8 for label in ("opust", "rabat"))


def section(text):
    key = normalized(text)
    if fiscal_marker(text):
        return "items"
    if key.startswith("opakowania"):
        if "wydania" in key:
            return "deposits"
        if "przyjecia" in key:
            return "deposit_returns"
        return "summary"
    if key.startswith(("do zap", "rozliczenie", "niefiskalny")):
        return "finished" if key.startswith(("rozliczenie", "niefiskalny")) else "summary"
    if key.startswith(("podsum", "podsun", "sprzed", "suma", "opusty", "rabaty", "sp:", "ptu", "kwota ptu")):
        return "summary"
    return None


def purchase(row: Row) -> ReceiptItem | None:
    text = row.text.replace("−", "-")
    match = PURCHASE.search(text)
    fallback = False
    if match is None:
        if discount_marker(text):
            return None
        match = PRICE.search(text)
        fallback = True
        if match is not None and not any(PRICE.search(fragment.text) for _, fragment in row.members):
            # A detached tax/marker glyph next to a previous item's final amount
            # is not enough evidence for a new purchase with a missing quantity.
            return None
    if match is None:
        money = list(MONEY.finditer(text))
        if (len(money) >= 2 and section(text) is None
                and not discount_marker(text)
                and re.search(r"\d\s*[A-G]\s*$", text, re.IGNORECASE)):
            name = clean_name(text[:money[0].start()])
            if name and any(char.isalpha() for char in name):
                # A damaged multiplier must not erase the purchase. Do not
                # interpret the earlier number as a price or guess a quantity.
                return ReceiptItem(name=name, before_discount=decimal(money[-1].group()))
        return None
    prefix = text[:match.start()]
    if fallback:
        prefix = re.sub(r"\s+\S*[.,]\d+\s*$", "", prefix)
    tail = amounts(text[match.end():])
    if discount_marker(text) and tail and tail[0] < 0:
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
        self.ocr = upright_ocr(ocr)
        self.rows = []
        for row in receipt_rows(self.ocr):
            sections = tuple(member for member in row.members if section(member[1].text))
            body = tuple(member for member in row.members if member not in sections)
            if sections and body and any(PURCHASE.search(fragment.text) for _, fragment in body):
                heading, purchase_row = Row(row.page, sections), Row(row.page, body)
                # Numeric text can share a row with a section on a curved page.
                # Purchases precede summaries, but follow section-opening labels.
                self.rows.extend((purchase_row, heading) if section(heading.text) == "summary"
                                 else (heading, purchase_row))
            else:
                self.rows.append(row)
        self.data = ReceiptData()
        self.warnings = []
        self.used_names = set()
        self.ambiguous_fields = set()
        self.saw_amount_due = False
        self.confirmation = False

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

    def field_amounts(self, row, is_label):
        """Read totals against their original labels, independent of row grouping."""
        labels = [(index, fragment) for index, fragment in row.members if is_label(normalized(fragment.text))]
        if not labels:
            return amounts(row.text), [row]
        values, evidence = [], []
        for index, label in labels:
            direct = amounts(label.text)
            label_row = Row(row.page, ((index, label),))
            if direct:
                values.extend(direct)
                evidence.append(label_row)
                continue
            candidates = []
            for page in self.ocr.pages:
                if page.page_number != row.page:
                    continue
                for fi, fragment in enumerate(page.lines):
                    if not re.fullmatch(rf"\s*{AMOUNT}\s*(?:PLN|zł)?\s*", fragment.text, re.IGNORECASE):
                        continue
                    fx, fy = center(fragment)
                    if fx < center(label)[0]:
                        continue
                    gap = abs(y_at(label, fx) - fy) / max(height(label), height(fragment))
                    if gap <= 1.25:
                        font_gap = abs(height(label) - height(fragment)) / max(height(label), height(fragment))
                        candidates.append((gap + font_gap, fi, fragment))
            if candidates:
                candidates.sort(key=lambda entry: entry[0])
                best = candidates[0][0]
                # Close competing candidates stay ambiguous instead of picking
                # whichever happened to be first in the OCR list.
                for gap, fi, fragment in candidates:
                    if gap <= best + 0.15:
                        values.extend(amounts(fragment.text))
                        evidence.extend([label_row, Row(row.page, ((fi, fragment),))])
        return values, evidence

    def document_fields(self, start):
        totals, dues, dates, currencies = [], [], [], []
        total_rows, due_rows, date_rows, currency_rows = [], [], [], []
        ended = False
        current_page = self.rows[start].page
        scan_start = 0 if self.confirmation else start
        for index, row in enumerate(self.rows[scan_start:]):
            if row.page != current_page:
                current_page, ended = row.page, False
            key = normalized(row.text)
            if index and fiscal_marker(row.text) and row.page == self.rows[start].page:
                break  # A second fiscal document is not a continuation of this one.
            if "niefiskalny" in key and not self.confirmation:
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
            is_total = lambda text: bool(re.search(r"\bsum\w?\s*:?\s+(?:pln|zl)\b", text))
            if is_total(key):
                target = (totals, total_rows)
            elif key.startswith("do zap"):
                self.saw_amount_due = True
                target = (dues, due_rows)
            if target is not None:
                values, evidence = self.field_amounts(
                    row, is_total if target[0] is totals else lambda text: text.startswith("do zap"))
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
        printed_totals = (amounts(numeric.text[numeric_match.end():]) if numeric_match
                          else [item.before_discount] if item.before_discount is not None else [])
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
            if not previous.members or previous.page != first.page or any(ref in self.used_names for ref in previous.refs):
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
            bottom = min(bottom, y_at(next_numeric, nx) - 0.1 * height(next_numeric))
        fragment_rows = []
        for page in self.ocr.pages:
            if page.page_number != first.page:
                continue
            for fi, fragment in enumerate(page.lines):
                fy = y_at(fragment, nx)
                if ny + 0.45 * height(numeric) < fy < bottom:
                    fragment_rows.append(Row(first.page, ((fi, fragment),)))
        # A discount label establishes the role of an adjacent amount, even if
        # OCR omitted its minus sign. Do not confuse it with the following net.
        labels = [fragment for row in rows for _, fragment in row.members
                  if discount_marker(fragment.text)
                  and section(fragment.text) is None]
        labelled = set()
        gross = item.before_discount
        if gross is None and item.quantity is not None and item.unit_price is not None and not ambiguous_gross:
            gross = (item.quantity * item.unit_price).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
        for label in labels:
            candidates = []
            for row in fragment_rows:
                fragment = row.members[0][1]
                if not (AMOUNT_ONLY.fullmatch(row.text) or fragment is label) or not amounts(row.text):
                    continue
                x = (center(label)[0] + center(fragment)[0]) / 2
                gap = abs(y_at(label, x) - y_at(fragment, x)) / min(height(label), height(fragment))
                values = amounts(row.text)
                if all(value >= 0 for value in values):
                    # A missing minus is recoverable only when another printed
                    # amount confirms gross - discount = net. Proximity alone
                    # must never turn a final amount into a discount.
                    if gross is None or not any(
                        gross - value == decimal(match.group(1))
                        for value in values for other in fragment_rows if other is not row
                        and (match := AMOUNT_ONLY.fullmatch(other.text))
                        and decimal(match.group(1)) >= 0
                    ):
                        continue
                if gap <= 0.75:
                    candidates.append((gap, row.refs))
            if candidates:
                labelled.add(min(candidates)[1])
        def discount_label(row):
            return row.refs in labelled
        discounts = [(row, -abs(value)) for row in fragment_rows
                     for value in amounts(row.text) if value < 0 or discount_label(row)]
        opust = [row for row in rows if discount_marker(row.text)]
        has_discount = bool(discounts or opust)
        discount_rows = [row for row, _ in discounts]
        if has_discount:
            item.discount = sum((value for _, value in discounts), Decimal("0")) if discounts else None
            if item.discount is None:
                self.warn("missing_discount", "Discount is present but its amount is unreadable", path + ".discount")
        else:
            item.discount = Decimal("0.00")
        totals = [(row, decimal(m.group(1))) for row in fragment_rows
                  if not discount_label(row) and (m := AMOUNT_ONLY.fullmatch(row.text))
                  and decimal(m.group(1)) >= 0]
        if gross is not None and not ambiguous_gross:
            for final_row, value in sorted(totals, key=lambda entry: y_at(entry[0].members[0][1], nx)):
                final_y = y_at(final_row.members[0][1], nx)
                prefix = [(row, amount) for row, amount in discounts
                          if y_at(row.members[0][1], nx) < final_y]
                if prefix and gross + sum((amount for _, amount in prefix), Decimal(0)) == value:
                    if len(prefix) != len(discounts):
                        self.warn("unassigned_discount", "Discount after a completed purchase was not assigned to it", "items")
                    discounts, totals = prefix, [(final_row, value)]
                    discount_rows = [row for row, _ in prefix]
                    item.discount = sum((amount for _, amount in prefix), Decimal(0))
                    break
        if self.confirmation:
            # Transaction tables print net, tax rate, tax, then gross. Those
            # columns are not three competing product totals.
            table = next((row for row in rows[1:] if "%" in row.text and len(amounts(row.text)) >= 3), None)
            if table is not None:
                totals = [(table, amounts(table.text)[-1])]
        if item.unit_price is None and labels and not discounts:
            # A partial purchase with an unreadable discount has no independent
            # price/quantity check to distinguish a lone discount from a net.
            totals = []
            item.discount = None
            self.warn("ambiguous_item_amount", "Cannot distinguish discount from net on a partial purchase", path + ".total")
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
            signed = any(value < 0 for row in discount_rows for value in amounts(row.text))
            self.record(path + ".discount", discount_rows or [first], "observed" if signed else "derived")
        if item.name is None:
            self.warn("missing_item_name", "Purchase amounts have no readable product name", path + ".name")
        if item.quantity is None:
            self.warn("missing_quantity", "Purchase quantity is unreadable", path + ".quantity")
        if item.unit_price is None:
            self.warn("missing_unit_price", "Purchase unit price is unreadable", path + ".unit_price")
        if item.total is None:
            self.warn("missing_item_amount", "Purchase final amount could not be established", path + ".total")
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
        start = next((i for i, row in enumerate(self.rows) if fiscal_marker(row.text)), None)
        if start is None:
            purchases = [i for i, row in enumerate(self.rows) if purchase(row)]
            text = normalized(" ".join(row.text for row in self.rows))
            self.confirmation = "potwierdzenie transakcji" in text
            if self.confirmation:
                heading = next(i for i, row in enumerate(self.rows) if "potwierdzenie" in normalized(row.text))
                purchases = [index for index in purchases if index > heading]
            has_total = bool(re.search(r"\bsum\w?\s*:?\s+(?:pln|zl)\b", text))
            if not purchases or not has_total or not (self.confirmation or re.search(r"\bnip\b", text)):
                self.warn("missing_receipt_section", "No supported receipt structure found")
                return StepResult(self.data, tuple(self.warnings))
            start = purchases[0]
            self.rows.insert(start, Row(self.rows[start].page, ()))
            self.warn("inferred_receipt_section", "Purchase section inferred from purchases, seller and total")
        marker = self.rows[start]
        remaining = tuple(member for member in marker.members if not fiscal_marker(member[1].text))
        if remaining and purchase(Row(marker.page, remaining)):
            self.rows[start] = Row(marker.page, tuple(member for member in marker.members if member not in remaining))
            self.rows.insert(start + 1, Row(marker.page, remaining))
        self.header(start)
        self.document_fields(start)
        state, block, pending = "items", [], None
        current_page = self.rows[start].page
        saw_deposits = False
        summary_discounts = []
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
            elif state == "summary" and normalized(row.text).startswith(("opust ", "rabat ")):
                summary_discounts.extend((row, value) for value in amounts(row.text) if value < 0)
        if block:
            self.item_block(block)
        if pending is not None:
            self.warn("unparsed_deposit", "Deposit label has no readable amount", state)
        if summary_discounts:
            adjustment = sum((value for _, value in summary_discounts), Decimal(0))
            item = self.data.items[0] if len(self.data.items) == 1 else None
            if (item is not None and item.before_discount is not None and item.discount == 0
                    and item.before_discount + adjustment == self.data.total):
                item.discount, item.total = adjustment, self.data.total
                self.record("items[0].discount", [row for row, _ in summary_discounts])
                refs = (*self.data.evidence["items[0].before_discount"].fragments,
                        *self.data.evidence["items[0].discount"].fragments)
                self.data.evidence["items[0].total"] = FieldEvidence(tuple(dict.fromkeys(refs)), "derived")
            else:
                self.warn("unassigned_discount", "Receipt-level discount was not allocated to individual products", "items")
        if (self.data.amount_due is None and self.data.total is not None
                and not saw_deposits and not self.saw_amount_due):
            self.data.amount_due = self.data.total
            self.data.evidence["amount_due"] = FieldEvidence(self.data.evidence["total"].fragments, "derived")
        elif self.data.amount_due is None and "amount_due" not in self.ambiguous_fields:
            self.warn("missing_amount_due", "Amount due could not be established", "amount_due")
        return StepResult(self.data, tuple(self.warnings))


def parse_receipt(ocr: OCRResult) -> StepResult:
    return ReceiptParser(ocr).run()
