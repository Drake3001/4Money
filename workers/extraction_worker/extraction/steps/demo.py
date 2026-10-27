from datetime import date
from decimal import Decimal

from ..contracts import (
    ExtractionWarning,
    PreparedDocument,
    ReceiptData,
    ReceiptItem,
    StepResult,
)


class DemoStep:
    input_type = PreparedDocument
    output_type = ReceiptData
    config = {}
    name = "demo"
    version = "1"

    def run(self, document: PreparedDocument) -> StepResult:
        return StepResult(
            output=ReceiptData(
                store="DEMO",
                date=date(2026, 1, 1),
                currency="PLN",
                total=Decimal("12.50"),
                items=[
                    ReceiptItem(
                        "Przykładowy produkt",
                        Decimal("1"),
                        "szt",
                        Decimal("12.50"),
                        Decimal("12.50"),
                    )
                ],
            ),
            warnings=(
                ExtractionWarning(
                    "demo_result",
                    "Demonstration data only: this document was not recognized",
                ),
            ),
        )
