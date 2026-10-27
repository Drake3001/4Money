"""Shared OCR result conversion; engines only recognize individual pages."""

import math
from importlib.metadata import PackageNotFoundError, version
from pathlib import Path

from ...contracts import (
    ExtractionWarning,
    OCRFragment,
    OCRPage,
    OCRResult,
    PreparedDocument,
    StepResult,
)

MODEL_ROOT = Path(__file__).resolve().parents[3] / ".cache" / "models"


def package_version(*names):
    values = []
    for name in names:
        try:
            values.append(f"{name}={version(name)}")
        except PackageNotFoundError:
            values.append(f"{name}=not-installed")
    return ";".join(values)


def fragment(text, score, box):
    if not isinstance(text, str):
        raise TypeError("OCR text must be a string")
    score = float(score)
    points = tuple(tuple(float(coordinate) for coordinate in point) for point in box)
    if len(points) != 4 or any(len(point) != 2 for point in points):
        raise ValueError("OCR box must contain four 2D points")
    if not math.isfinite(score) or any(not math.isfinite(v) for p in points for v in p):
        raise ValueError("OCR returned non-finite geometry or confidence")
    return OCRFragment(text, max(0.0, min(1.0, score)), points)


class LocalOCRStep:
    input_type = PreparedDocument
    output_type = OCRResult

    def run(self, document: PreparedDocument) -> StepResult:
        self.initialize()
        pages = []
        warnings = []
        for page in document.pages:
            lines = tuple(self.recognize(page.content))
            pages.append(OCRPage(
                page.number, page.source_name, page.source_index,
                page.source_page_number, page.width, page.height, lines,
            ))
            if not lines:
                warnings.append(ExtractionWarning("no_text", f"No text detected on page {page.number}"))
            elif any(line.confidence < 0.8 for line in lines):
                warnings.append(ExtractionWarning(
                    "low_confidence",
                    f"Page {page.number} contains OCR fragments with confidence below 0.8",
                ))
        return StepResult(
            OCRResult(document, tuple(pages), self.name, self.version, dict(self.config)),
            tuple(warnings),
        )
