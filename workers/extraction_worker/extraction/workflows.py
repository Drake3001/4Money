"""Analysis workflow factories. Callers explicitly supply configured API clients."""

from .pipeline import Pipeline
from .steps.analysis import ClassifyReceiptItemsStep, AnalyzeReceiptImagesStep, AnalyzeReceiptImagesWithOCRStep, ParseReceiptContextStep
from .steps.validate import ValidateAnalyzedReceiptStep


def text_analysis_pipeline(client, *, parser=None, prefix=()):
    """OCRResult → ParsedReceipt → AnalyzedReceipt; optionally prepend OCR steps."""
    return Pipeline([*prefix, ("parse", ParseReceiptContextStep(parser)),
                     ("classify", ClassifyReceiptItemsStep(client)), ("validate", ValidateAnalyzedReceiptStep())])


def vision_analysis_pipeline(client):
    """Original SourceFiles → AnalyzedReceipt, using the same arithmetic validator."""
    return Pipeline([("analyze", AnalyzeReceiptImagesStep(client)), ("validate", ValidateAnalyzedReceiptStep())])


def vision_ocr_analysis_pipeline(client):
    return Pipeline([("analyze", AnalyzeReceiptImagesWithOCRStep(client)), ("validate", ValidateAnalyzedReceiptStep())])
