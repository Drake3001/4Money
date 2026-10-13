"""Validation reports issues without modifying extracted data."""

from decimal import Decimal

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

    finite_amounts = True
    numbers = [("total", data.total)]
    for index, item in enumerate(data.items):
        numbers.extend(
            (f"items[{index}].{name}", getattr(item, name))
            for name in ("quantity", "unit_price", "total")
        )
    for field, value in numbers:
        if value is not None and not value.is_finite():
            warnings.append(
                ExtractionWarning("invalid_number", "Number is not finite", field)
            )
            finite_amounts = False

    if (
        finite_amounts
        and data.total is not None
        and data.items
        and all(item.total is not None for item in data.items)
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
    return warnings


class ValidateReceiptStep:
    input_type = ReceiptData
    output_type = ReceiptData
    version = "1"
    config = {"total_tolerance": "0.01"}

    def run(self, input):
        return StepResult(input, tuple(validate_receipt(input)))
