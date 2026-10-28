"""Shared contracts, independent of filesystem, transport and providers."""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from datetime import date
from decimal import Decimal
from typing import Any, Protocol


@dataclass(frozen=True)
class SourceFile:
    content: bytes
    mime_type: str
    name: str


@dataclass(frozen=True)
class PreparationConfig:
    pdf_dpi: float = 200
    max_pages: int | None = None
    max_page_pixels: int | None = None
    max_document_pixels: int | None = None

    def __post_init__(self) -> None:
        if not math.isfinite(self.pdf_dpi) or self.pdf_dpi <= 0:
            raise ValueError("pdf_dpi must be a finite positive number")
        for name in ("max_pages", "max_page_pixels", "max_document_pixels"):
            value = getattr(self, name)
            if value is not None and (type(value) is not int or value <= 0):
                raise ValueError(f"{name} must be a positive integer or None")


@dataclass(frozen=True)
class DocumentPage:
    content: bytes  # RGB PNG
    number: int  # One-based position in the complete document.
    source_name: str
    source_index: int  # One-based input index; names need not be unique.
    source_page_number: int  # One for an image; original page number for PDF.
    width: int
    height: int
    mime_type: str = "image/png"


@dataclass(frozen=True)
class PreparedDocument:
    sources: tuple[SourceFile, ...]
    pages: tuple[DocumentPage, ...]


@dataclass
class ReceiptItem:
    name: str | None = None
    quantity: Decimal | None = None
    unit: str | None = None
    unit_price: Decimal | None = None
    total: Decimal | None = None
    before_discount: Decimal | None = None
    discount: Decimal | None = None


@dataclass
class ReceiptDeposit:
    name: str | None = None
    quantity: Decimal | None = None
    unit_price: Decimal | None = None
    total: Decimal | None = None


@dataclass(frozen=True)
class FieldEvidence:
    # (one-based page number, zero-based index in the original OCRPage.lines).
    fragments: tuple[tuple[int, int], ...] = ()
    method: str = "observed"


@dataclass
class ReceiptData:
    store: str | None = None
    date: date | None = None
    currency: str | None = None
    total: Decimal | None = None
    items: list[ReceiptItem] = field(default_factory=list)
    amount_due: Decimal | None = None
    deposits: list[ReceiptDeposit] = field(default_factory=list)
    deposit_returns: list[ReceiptDeposit] = field(default_factory=list)
    evidence: dict[str, FieldEvidence] = field(default_factory=dict)


@dataclass(frozen=True)
class ExtractionWarning:
    code: str
    message: str
    field: str | None = None


@dataclass(frozen=True)
class SourceFiles:
    files: tuple[SourceFile, ...]

    def __post_init__(self):
        object.__setattr__(self, "files", tuple(self.files))
        if not self.files:
            raise ValueError("At least one source file is required")


@dataclass(frozen=True)
class OCRFragment:
    text: str
    confidence: float
    box: tuple[tuple[float, float], ...]


@dataclass(frozen=True)
class OCRPage:
    page_number: int
    source_name: str
    source_index: int
    source_page_number: int
    width: int
    height: int
    lines: tuple[OCRFragment, ...]

    @property
    def text(self):
        return "\n".join(line.text for line in self.lines)


@dataclass(frozen=True)
class OCRResult:
    document: PreparedDocument
    pages: tuple[OCRPage, ...]
    engine: str
    version: str
    settings: dict
    coordinate_system: str = (
        "pixels of the prepared page passed to OCR; origin top-left"
    )


@dataclass(frozen=True)
class ParsedReceipt:
    """Parser output plus the OCR context needed by a following enrichment step."""

    receipt: ReceiptData
    ocr: OCRResult


@dataclass(frozen=True)
class ImageOCRInput:
    sources: SourceFiles
    ocr: OCRResult


@dataclass(frozen=True)
class ItemAnnotation:
    id: int
    name_raw: str | None
    name_corrected: str | None
    category: str | None
    needs_review: bool
    review_reason: str | None


@dataclass(frozen=True)
class ImageEvidence:
    """Model-declared image references; deliberately separate from OCR evidence."""

    field: str
    images: tuple[int, ...]


@dataclass(frozen=True)
class AnalyzedReceipt:
    """Shared text/vision output. Proposed names never overwrite extracted facts."""

    receipt: ReceiptData
    annotations: tuple[ItemAnnotation, ...]
    image_evidence: tuple[ImageEvidence, ...] = ()

    def __post_init__(self):
        object.__setattr__(self, "annotations", tuple(self.annotations))
        object.__setattr__(self, "image_evidence", tuple(self.image_evidence))
        if len(self.annotations) != len(self.receipt.items):
            raise ValueError("Annotations must cover every receipt item")
        for index, (annotation, item) in enumerate(zip(self.annotations, self.receipt.items)):
            if type(annotation.id) is not int or annotation.id != index or annotation.name_raw != item.name:
                raise ValueError("Annotation identities must match receipt item order and raw names")


@dataclass(frozen=True)
class StepResult:
    output: Any
    warnings: tuple[ExtractionWarning, ...] = ()


class Step(Protocol):
    input_type: type
    output_type: type
    version: str
    config: dict

    def run(self, input: Any) -> StepResult: ...


@dataclass(frozen=True)
class StepExecution:
    name: str
    implementation: str
    version: str
    config: dict
    seconds: float
    completed: bool


@dataclass(frozen=True)
class PipelineError:
    step: str
    exception_type: str
    message: str
    code: str | None = None
    details: dict | None = None


@dataclass
class PipelineResult:
    output: Any = None
    steps: list[StepExecution] = field(default_factory=list)
    warnings: list[ExtractionWarning] = field(default_factory=list)
    error: PipelineError | None = None
    captured_steps: dict[str, Any] = field(default_factory=dict, repr=False)

    @property
    def ok(self):
        return self.error is None
