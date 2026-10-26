"""Application-facing receipt schema, independent of pipeline diagnostics."""

from datetime import date
from decimal import Decimal

from pydantic import BaseModel

from extraction.contracts import ExtractionWarning, ReceiptDeposit
from extraction.reporting import to_dict


class ItemResponse(BaseModel):
    name: str | None
    quantity: Decimal | None
    unit: str | None
    unit_price: Decimal | None
    total: Decimal | None
    before_discount: Decimal | None
    discount: Decimal | None
    name_corrected: str | None
    category: str | None
    needs_review: bool
    review_reason: str | None


class ReceiptResponse(BaseModel):
    store: str | None
    date: date | None
    currency: str | None
    total: Decimal | None
    items: list[ItemResponse]
    amount_due: Decimal | None
    deposits: list[ReceiptDeposit]
    deposit_returns: list[ReceiptDeposit]


class ExtractionResponse(BaseModel):
    receipt: ReceiptResponse
    warnings: list[ExtractionWarning]
    needs_review: bool


def application_response(result):
    analyzed = result.output
    receipt = to_dict(analyzed.receipt)
    receipt.pop("evidence")
    for item, annotation in zip(receipt["items"], analyzed.annotations):
        item.update({key: getattr(annotation, key) for key in (
            "name_corrected", "category", "needs_review", "review_reason",
        )})
    return ExtractionResponse(
        receipt=ReceiptResponse(**receipt), warnings=result.warnings,
        needs_review=bool(result.warnings) or any(a.needs_review for a in analyzed.annotations),
    )
