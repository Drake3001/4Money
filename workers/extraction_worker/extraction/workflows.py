"""Analysis workflows composed with configured receipt tasks."""

from .pipeline import Pipeline
from .steps.analysis import ClassifyReceiptItemsStep, AnalyzeReceiptImagesStep, AnalyzeReceiptImagesWithOCRStep, ParseReceiptContextStep
from .steps.validate import ValidateAnalyzedReceiptStep


def text_analysis_pipeline(classifier, *, parser=None, prefix=()):
    """OCRResult → ParsedReceipt → AnalyzedReceipt; optionally prepend OCR steps."""
    return Pipeline([*prefix, ("parse", ParseReceiptContextStep(parser)),
                     ("classify", ClassifyReceiptItemsStep(classifier)), ("validate", ValidateAnalyzedReceiptStep())])


def vision_analysis_pipeline(analyzer):
    """Original SourceFiles → AnalyzedReceipt, using the same arithmetic validator."""
    return Pipeline([("analyze", AnalyzeReceiptImagesStep(analyzer)), ("validate", ValidateAnalyzedReceiptStep())])


def vision_ocr_analysis_pipeline(analyzer):
    return Pipeline([("analyze", AnalyzeReceiptImagesWithOCRStep(analyzer)), ("validate", ValidateAnalyzedReceiptStep())])
