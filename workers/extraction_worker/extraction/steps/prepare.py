"""Decode ordered sources into ordered RGB PNG pages."""

from contextlib import closing
from io import BytesIO
from typing import Sequence

import pypdfium2 as pdfium
from PIL import Image, ImageOps

from ..contracts import (
    DocumentPage,
    PreparationConfig,
    PreparedDocument,
    SourceFile,
    SourceFiles,
    StepResult,
)


class PreparationError(ValueError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


def _page(
    image: Image.Image,
    source: SourceFile,
    source_index: int,
    source_page: int,
    number: int,
) -> DocumentPage:
    with image.convert("RGB") as rgb:
        output = BytesIO()
        rgb.save(output, format="PNG")
        return DocumentPage(
            content=output.getvalue(),
            number=number,
            source_name=source.name,
            source_index=source_index,
            source_page_number=source_page,
            width=rgb.width,
            height=rgb.height,
        )


def _open_pdf(source: SourceFile) -> pdfium.PdfDocument:
    # The high-level bytes loader rejects zero-page PDFs using the last global
    # error code, which can be stale. Check the handle before querying pages.
    # The caller retains source.content until this document has been closed.
    handle = pdfium.raw.FPDF_LoadMemDocument64(
        source.content, len(source.content), None
    )
    if not handle:
        error = pdfium.raw.FPDF_GetLastError()
        code = (
            "password_required"
            if error == pdfium.raw.FPDF_ERR_PASSWORD
            else "invalid_file"
        )
        raise PreparationError(
            code, f"Cannot open PDF {source.name} (PDFium error {error})"
        )
    return pdfium.PdfDocument(handle)


def prepare_document(
    sources: Sequence[SourceFile], config: PreparationConfig | None = None
) -> PreparedDocument:
    config = config if config is not None else PreparationConfig()
    originals = tuple(sources)
    if not originals:
        raise PreparationError("empty_document", "At least one source file is required")
    pages: list[DocumentPage] = []
    for index, source in enumerate(originals, start=1):
        if not source.content:
            raise PreparationError("empty_file", f"Empty source: {source.name}")
        if source.mime_type not in {"image/jpeg", "image/png", "application/pdf"}:
            raise PreparationError(
                "unsupported_type", f"Unsupported MIME type: {source.mime_type}"
            )
        try:
            if source.mime_type == "application/pdf":
                with _open_pdf(source) as pdf:
                    if not len(pdf):
                        raise PreparationError(
                            "empty_document", f"PDF has no pages: {source.name}"
                        )
                    for page_index in range(len(pdf)):
                        with closing(pdf[page_index]) as pdf_page:
                            with closing(
                                pdf_page.render(scale=config.pdf_dpi / 72)
                            ) as bitmap:
                                with bitmap.to_pil() as image:
                                    pages.append(
                                        _page(
                                            image,
                                            source,
                                            index,
                                            page_index + 1,
                                            len(pages) + 1,
                                        )
                                    )
            else:
                with Image.open(BytesIO(source.content)) as image:
                    expected = "JPEG" if source.mime_type == "image/jpeg" else "PNG"
                    # Phone JPEGs can include additional gain-map images (MPO).
                    # Use their primary frame, like a normal photo viewer.
                    accepted = {"JPEG", "MPO"} if expected == "JPEG" else {"PNG"}
                    if image.format not in accepted:
                        raise PreparationError(
                            "invalid_file",
                            f"Image format does not match MIME type: {source.name}",
                        )
                    with ImageOps.exif_transpose(image) as oriented:
                        pages.append(_page(oriented, source, index, 1, len(pages) + 1))
        except PreparationError:
            raise
        except pdfium.PdfiumError as exc:
            code = (
                "password_required"
                if exc.err_code == pdfium.raw.FPDF_ERR_PASSWORD
                else "invalid_file"
            )
            raise PreparationError(
                code, f"Cannot prepare {source.name}: {exc}"
            ) from exc
        except Exception as exc:
            raise PreparationError(
                "invalid_file", f"Cannot prepare {source.name}: {exc}"
            ) from exc
    return PreparedDocument(sources=originals, pages=tuple(pages))


class PrepareDocumentStep:
    input_type = SourceFiles
    output_type = PreparedDocument
    version = "1"

    def __init__(self, pdf_dpi=200):
        self.settings = PreparationConfig(pdf_dpi)
        self.config = {"pdf_dpi": pdf_dpi}

    def run(self, input):
        return StepResult(prepare_document(input.files, self.settings))
