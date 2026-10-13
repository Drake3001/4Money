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

    def __post_init__(self) -> None:
        if not math.isfinite(self.pdf_dpi) or self.pdf_dpi <= 0:
            raise ValueError("pdf_dpi must be a finite positive number")


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


@dataclass
class ReceiptData:
    store: str | None = None
    date: date | None = None
    currency: str | None = None
    total: Decimal | None = None
    items: list[ReceiptItem] = field(default_factory=list)


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
