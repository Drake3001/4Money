"""Analysis workflows composed with configured receipt tasks."""

from typing import Literal

from .pipeline import Pipeline
from .steps.analysis import ClassifyReceiptItemsStep, AnalyzeReceiptImagesStep, AnalyzeReceiptImagesWithOCRStep, ParseReceiptContextStep
from .steps.validate import ValidateAnalyzedReceiptStep

Workflow = Literal["vision_ocr", "vision", "text"]
WORKFLOW_NAMES = ("vision_ocr", "vision", "text")


def warmup_service(pipelines):
    """Exercise shared local models before readiness, without any provider call."""
    from io import BytesIO
    from PIL import Image, ImageDraw, ImageFont
    from .contracts import SourceFile, SourceFiles

    with Image.new("RGB", (800, 400), "white") as image:
        draw = ImageDraw.Draw(image)
        font = ImageFont.load_default(size=28)
        for index, text in enumerate(("PARAGON FISKALNY", "MLEKO 1 SZT 3.50", "SUMA PLN 3.50")):
            draw.text((40, 40 + index * 80), text, fill="black", font=font)
        buffer = BytesIO()
        image.save(buffer, format="PNG")
    steps = dict(pipelines["vision_ocr"].steps)
    local = Pipeline([(name, steps[name]) for name in ("prepare", "orient", "ocr")])
    result = local.run(SourceFiles((SourceFile(buffer.getvalue(), "image/png", "warmup.png"),)))
    if not result.ok:
        raise RuntimeError(f"Local model warmup failed at {result.error.step}: {result.error.message}")


def service_pipelines(classifier, analyzer):
    """Full SourceFiles workflows, sharing lazy CPU engines under the API lock.

    Add new service variants here; HTTP handlers only select a named pipeline.
    Existing saved-OCR factories below remain independent of this composition.
    """
    from .steps.images import OCRImagesStep, PreparedImagesStep
    from .steps.prepare import PrepareDocumentStep
    from .steps.orientation import OrientationStep
    from .steps.ocr import RapidOCRStep

    images = [("prepare", PrepareDocumentStep()), ("orient", OrientationStep())]
    ocr = [*images, ("ocr", RapidOCRStep())]
    return {
        "vision_ocr": Pipeline([
            *ocr, ("images", OCRImagesStep()),
            ("analyze", AnalyzeReceiptImagesWithOCRStep(analyzer)),
            ("validate", ValidateAnalyzedReceiptStep()),
        ]),
        "vision": Pipeline([
            *images, ("images", PreparedImagesStep()),
            ("analyze", AnalyzeReceiptImagesStep(analyzer)),
            ("validate", ValidateAnalyzedReceiptStep()),
        ]),
        "text": text_analysis_pipeline(classifier, prefix=ocr),
    }


def text_analysis_pipeline(classifier, *, parser=None, prefix=()):
    """OCRResult → ParsedReceipt → AnalyzedReceipt; optionally prepend OCR steps."""
    return Pipeline([*prefix, ("parse", ParseReceiptContextStep(parser)),
                     ("classify", ClassifyReceiptItemsStep(classifier)), ("validate", ValidateAnalyzedReceiptStep())])


def vision_analysis_pipeline(analyzer):
    """Original SourceFiles → AnalyzedReceipt, using the same arithmetic validator."""
    return Pipeline([("analyze", AnalyzeReceiptImagesStep(analyzer)), ("validate", ValidateAnalyzedReceiptStep())])


def vision_ocr_analysis_pipeline(analyzer):
    return Pipeline([("analyze", AnalyzeReceiptImagesWithOCRStep(analyzer)), ("validate", ValidateAnalyzedReceiptStep())])
