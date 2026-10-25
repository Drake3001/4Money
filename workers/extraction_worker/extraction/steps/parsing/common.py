"""Provider-independent receipt schema and strict field decoding, without OCR."""

import re
from dataclasses import fields
from datetime import date
from decimal import Decimal

from ...contracts import ExtractionWarning, ReceiptData, ReceiptDeposit, ReceiptItem, StepResult


def object_schema(properties):
    return {"type": "object", "properties": properties,
            "required": list(properties), "additionalProperties": False}


TEXT = {"type": ["string", "null"]}
NUMERIC = {**TEXT, "description": "Decimal string with dot separator, or null."}
ITEM_SCHEMA = object_schema({field.name: TEXT if field.name in ("name", "unit") else NUMERIC
                       for field in fields(ReceiptItem)})
DEPOSIT_SCHEMA = object_schema({field.name: TEXT if field.name == "name" else NUMERIC
                          for field in fields(ReceiptDeposit)})
RECEIPT_SCHEMA = object_schema({
    "store": TEXT, "date": {**TEXT, "description": "YYYY-MM-DD or null"}, "currency": TEXT,
    "total": NUMERIC, "amount_due": NUMERIC,
    "items": {"type": "array", "items": ITEM_SCHEMA},
    "deposits": {"type": "array", "items": DEPOSIT_SCHEMA},
    "deposit_returns": {"type": "array", "items": DEPOSIT_SCHEMA},
    "issues": {"type": "array", "items": {"type": "string"}},
})


def _number(value, *, money):
    if value is None:
        return None
    pattern = r"-?\d+\.\d{2}" if money else r"-?\d+(?:\.\d+)?"
    if not isinstance(value, str) or not re.fullmatch(pattern, value):
        raise ValueError("Model returned an invalid decimal string")
    return Decimal(value)


def decode_receipt_fields(raw):
    if not isinstance(raw, dict) or set(raw) != set(RECEIPT_SCHEMA["properties"]):
        raise ValueError("Model receipt does not match the expected fields")
    for name in ("store", "currency", "date"):
        if raw[name] is not None and not isinstance(raw[name], str):
            raise ValueError("Model returned invalid receipt text")
    if raw["date"] is not None and not re.fullmatch(r"\d{4}-\d{2}-\d{2}", raw["date"]):
        raise ValueError("Model returned an invalid date")
    receipt = ReceiptData(store=raw["store"], currency=raw["currency"],
                          date=date.fromisoformat(raw["date"]) if raw["date"] else None,
                          total=_number(raw["total"], money=True),
                          amount_due=_number(raw["amount_due"], money=True))
    for group, cls in (("items", ReceiptItem), ("deposits", ReceiptDeposit), ("deposit_returns", ReceiptDeposit)):
        if not isinstance(raw[group], list):
            raise ValueError("Model receipt group must be an array")
        names = {field.name for field in fields(cls)}
        for entry in raw[group]:
            if not isinstance(entry, dict) or set(entry) != names:
                raise ValueError("Model entry does not match the expected fields")
            values = {}
            for name, value in entry.items():
                if name in ("name", "unit"):
                    if value is not None and not isinstance(value, str):
                        raise ValueError("Model entry text must be string or null")
                    values[name] = value
                else:
                    values[name] = _number(value, money=name != "quantity")
            getattr(receipt, group).append(cls(**values))
    if not isinstance(raw["issues"], list) or any(not isinstance(issue, str) for issue in raw["issues"]):
        raise ValueError("Model issues must be strings")
    return StepResult(receipt, tuple(ExtractionWarning("ai_uncertain", issue) for issue in raw["issues"]))


def populated_fields(receipt):
    result = {name for name in ("store", "currency", "date", "total", "amount_due")
              if getattr(receipt, name) is not None}
    for group in ("items", "deposits", "deposit_returns"):
        for index, item in enumerate(getattr(receipt, group)):
            result.update(f"{group}[{index}].{field.name}" for field in fields(item)
                          if getattr(item, field.name) is not None)
    return result
