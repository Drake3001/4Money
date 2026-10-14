"""Structural checks for manually transcribed receipt references."""

import re
from datetime import date, time
from decimal import Decimal


def validate_reference(value, source=None, sources=None):
    """Return field-level errors; this does not verify the photo's contents."""
    errors = []
    if not isinstance(value, dict):
        return ["$: expected an object"]

    def text(obj, key, prefix="", nullable=False):
        field = f"{prefix}{key}"
        if key not in obj:
            errors.append(f"{field}: required field")
        elif not (nullable and obj[key] is None) and (
            not isinstance(obj[key], str) or not obj[key].strip()
        ):
            errors.append(f"{field}: expected a nonempty string" + (" or null" if nullable else ""))

    def number(obj, key, prefix="", money=False, nonnegative=False, nullable=False):
        text(obj, key, prefix, nullable=nullable)
        raw = obj.get(key)
        if nullable and raw is None:
            return
        pattern = r"-?\d+\.\d{2}" if money else r"-?\d+(?:\.\d+)?"
        if isinstance(raw, str):
            if not re.fullmatch(pattern, raw):
                errors.append(f"{prefix}{key}: expected a decimal string" + (" with two decimal places" if money else ""))
            elif nonnegative and Decimal(raw) < 0:
                errors.append(f"{prefix}{key}: must be nonnegative")

    for key in ("source", "annotation_method", "store", "date", "time", "currency"):
        text(value, key)
    if source is not None and value.get("source") != source:
        errors.append(f"source: expected {source!r}")
    for key, parse in (("date", date.fromisoformat), ("time", time.fromisoformat)):
        raw = value.get(key)
        if isinstance(raw, str):
            pattern = r"\d{4}-\d{2}-\d{2}" if key == "date" else r"\d{2}:\d{2}(?::\d{2})?"
            try:
                parse(raw)
                if not re.fullmatch(pattern, raw):
                    raise ValueError()
            except ValueError:
                errors.append(f"{key}: invalid ISO {key}")
    if isinstance(value.get("currency"), str) and not re.fullmatch(r"[A-Z]{3}", value["currency"]):
        errors.append("currency: expected a three-letter uppercase code")
    for key in ("total", "amount_due"):
        number(value, key, money=True, nonnegative=True)
    items = value.get("items")
    if not isinstance(items, list) or not items:
        errors.append("items: expected a nonempty array")
    else:
        for index, item in enumerate(items):
            prefix = f"items[{index}]."
            if not isinstance(item, dict):
                errors.append(f"items[{index}]: expected an object")
                continue
            text(item, "name", prefix)
            text(item, "unit", prefix, nullable=True)
            number(item, "quantity", prefix, nonnegative=True)
            for key in ("unit_price", "before_discount", "discount", "total"):
                number(item, key, prefix, money=True, nonnegative=key != "discount")
            for key in ("tax_code", "printed_marker"):
                if key in item:
                    text(item, key, prefix, nullable=True)
    uncertainties = value.get("uncertain_fields")
    if not isinstance(uncertainties, list):
        errors.append("uncertain_fields: expected an array")
    else:
        for index, entry in enumerate(uncertainties):
            if not isinstance(entry, dict):
                errors.append(f"uncertain_fields[{index}]: expected an object")
            else:
                for key in ("field", "detail"):
                    text(entry, key, f"uncertain_fields[{index}].")
    for key in ("deposits", "deposit_returns", "tax_summary", "notes"):
        if key in value and not isinstance(value[key], list):
            errors.append(f"{key}: expected an array")
    if "payment" in value and not isinstance(value["payment"], dict):
        errors.append("payment: expected an object")
    elif "payment" in value:
        text(value["payment"], "label", "payment.")
        number(value["payment"], "amount", "payment.", money=True, nonnegative=True)
    if "tax_total" in value:
        number(value, "tax_total", money=True, nonnegative=True)
    if "receipt_number" in value:
        text(value, "receipt_number", nullable=True)
    if isinstance(value.get("notes"), list) and not all(isinstance(note, str) for note in value["notes"]):
        errors.append("notes: expected an array of strings")
    for key in ("deposits", "deposit_returns", "tax_summary"):
        entries = value.get(key)
        if not isinstance(entries, list):
            continue
        for index, entry in enumerate(entries):
            prefix = f"{key}[{index}]."
            if not isinstance(entry, dict):
                errors.append(f"{key}[{index}]: expected an object")
                continue
            if key in ("deposits", "deposit_returns"):
                text(entry, "name", prefix)
                number(entry, "quantity", prefix, nonnegative=True, nullable=True)
                for field in ("unit_price", "total"):
                    number(entry, field, prefix, money=True, nonnegative=key == "deposits",
                           nullable=field == "unit_price")
                    raw = entry.get(field)
                    if key == "deposit_returns" and isinstance(raw, str) and re.fullmatch(r"-?\d+\.\d{2}", raw) and Decimal(raw) > 0:
                        errors.append(f"{prefix}{field}: return amount must be nonpositive")
            else:
                text(entry, "code", prefix)
                number(entry, "rate_percent", prefix, nonnegative=True)
                for field in ("sales", "tax"):
                    number(entry, field, prefix, money=True, nonnegative=True)
    if sources is not None and value.get("sources") != sources:
        errors.append(f"sources: expected ordered filenames {sources!r}; review the whole receipt")
    elif "sources" in value and (
        not isinstance(value["sources"], list)
        or not all(isinstance(name, str) and name for name in value["sources"])
    ):
        errors.append("sources: expected an array of filenames")
    return errors
