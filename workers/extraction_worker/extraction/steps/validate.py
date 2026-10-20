"""Validation reports issues without modifying extracted data."""

from decimal import Decimal, ROUND_HALF_UP

from ..contracts import ExtractionWarning, ReceiptData, StepResult


def validate_receipt(data: ReceiptData) -> list[ExtractionWarning]:
    warnings: list[ExtractionWarning] = []
    for name in ("store", "date", "currency", "total"):
        value = getattr(data, name)
        if value is None or (isinstance(value, str) and not value.strip()):
            warnings.append(
                ExtractionWarning("missing_field", f"Missing receipt {name}", name)
            )
    if not data.items:
        warnings.append(
            ExtractionWarning("empty_items", "No receipt items were extracted", "items")
        )

    numbers = [("total", data.total), ("amount_due", data.amount_due)]
    for index, item in enumerate(data.items):
        numbers.extend(
            (f"items[{index}].{name}", getattr(item, name))
            for name in ("quantity", "unit_price", "before_discount", "discount", "total")
        )
    for group in ("deposits", "deposit_returns"):
        for index, entry in enumerate(getattr(data, group)):
            numbers.extend(
                (f"{group}[{index}].{name}", getattr(entry, name))
                for name in ("quantity", "unit_price", "total")
            )
    invalid_fields = set()
    for field, value in numbers:
        if value is not None and not value.is_finite():
            warnings.append(
                ExtractionWarning("invalid_number", "Number is not finite", field)
            )
            invalid_fields.add(field)

    for index, item in enumerate(data.items):
        prefix = f"items[{index}]"
        if all(
            getattr(item, name) is not None and f"{prefix}.{name}" not in invalid_fields
            for name in ("quantity", "unit_price", "before_discount")
        ):
            calculated = (item.quantity * item.unit_price).quantize(
                Decimal("0.01"), rounding=ROUND_HALF_UP
            )
            if abs(calculated - item.before_discount) > Decimal("0.01"):
                warnings.append(
                    ExtractionWarning(
                        "item_price_mismatch",
                        "Quantity times unit price differs from amount before discount",
                        f"{prefix}.before_discount",
                    )
                )
        if all(
            getattr(item, name) is not None and f"{prefix}.{name}" not in invalid_fields
            for name in ("before_discount", "discount", "total")
        ):
            if abs(item.before_discount + item.discount - item.total) > Decimal("0.01"):
                warnings.append(
                    ExtractionWarning(
                        "item_discount_mismatch",
                        "Amount before discount plus signed discount differs from item total",
                        f"{prefix}.total",
                    )
                )
    for group in ("deposits", "deposit_returns"):
        for index, entry in enumerate(getattr(data, group)):
            for name in ("unit_price", "total"):
                field = f"{group}[{index}].{name}"
                value = getattr(entry, name)
                if value is None or field in invalid_fields:
                    continue
                if (group == "deposits" and value < 0) or (group == "deposit_returns" and value > 0):
                    warnings.append(
                        ExtractionWarning(
                            "deposit_sign_mismatch",
                            "Issued deposits must be positive and returned deposits negative",
                            field,
                        )
                    )

    if (
        data.total is not None
        and "total" not in invalid_fields
        and data.items
        and all(
            item.total is not None and f"items[{index}].total" not in invalid_fields
            for index, item in enumerate(data.items)
        )
    ):
        items_total = sum((item.total for item in data.items), Decimal("0"))
        if abs(items_total - data.total) > Decimal("0.01"):
            warnings.append(
                ExtractionWarning(
                    "total_mismatch",
                    "Sum of item amounts differs from receipt total by more than 0.01",
                    "total",
                )
            )
    adjustments = [*data.deposits, *data.deposit_returns]
    if (
        data.total is not None
        and data.amount_due is not None
        and not invalid_fields.intersection({"total", "amount_due"})
        and all(entry.total is not None and entry.total.is_finite() for entry in adjustments)
    ):
        due = data.total + sum((entry.total for entry in adjustments), Decimal("0"))
        if abs(due - data.amount_due) > Decimal("0.01"):
            warnings.append(
                ExtractionWarning(
                    "amount_due_mismatch",
                    "Goods total plus deposits and signed returns differs from amount due by more than 0.01",
                    "amount_due",
                )
            )
    return warnings


class ValidateReceiptStep:
    input_type = ReceiptData
    output_type = ReceiptData
    version = "2"
    config = {"total_tolerance": "0.01"}

    def run(self, input):
        return StepResult(input, tuple(validate_receipt(input)))
