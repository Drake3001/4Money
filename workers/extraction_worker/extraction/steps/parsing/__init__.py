"""Receipt parsing steps sharing the OCRResult to ReceiptData contract."""

from ...contracts import OCRResult, ReceiptData
from .gemini import GeminiParseStep
from .rules import parse_receipt


class ParseReceiptStep:
    input_type = OCRResult
    output_type = ReceiptData
    version = "3"
    config = {"locale": "pl", "row_tolerance_in_heights": 0.55,
              "orientation": "receipt_anchors"}

    def run(self, input: OCRResult):
        return parse_receipt(input)
