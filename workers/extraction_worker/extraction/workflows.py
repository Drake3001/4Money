"""Analysis workflow factories. Callers explicitly supply configured API clients."""

from .pipeline import Pipeline
from .steps.analysis import EnrichReceiptStep, GemmaVisionStep, GemmaVisionOCRStep, ParseReceiptContextStep
from .steps.validate import ValidateAnalyzedReceiptStep


def text_analysis_pipeline(client, *, parser=None, prefix=()):
    """OCRResult → ParsedReceipt → AnalyzedReceipt; optionally prepend OCR steps."""
    return Pipeline([*prefix, ("parse", ParseReceiptContextStep(parser)),
                     ("enrich", EnrichReceiptStep(client)), ("validate", ValidateAnalyzedReceiptStep())])


def vision_analysis_pipeline(client):
    """Original SourceFiles → AnalyzedReceipt, using the same arithmetic validator."""
    return Pipeline([("parse", GemmaVisionStep(client)), ("validate", ValidateAnalyzedReceiptStep())])


def vision_ocr_analysis_pipeline(client):
    return Pipeline([("parse", GemmaVisionOCRStep(client)), ("validate", ValidateAnalyzedReceiptStep())])
