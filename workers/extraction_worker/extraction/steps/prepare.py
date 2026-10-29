"""Decode ordered sources into ordered RGB PNG pages."""

from contextlib import ExitStack, closing
from io import BytesIO
import math
from typing import Sequence

import pypdfium2 as pdfium
from PIL import Image, ImageOps, UnidentifiedImageError
from pillow_heif import register_heif_opener

from ..contracts import DocumentPage, PreparationConfig, PreparedDocument, SourceFile, SourceFiles, StepResult
from ..formats import IMAGE_FORMATS, has_image_signature, is_pdf

register_heif_opener(thumbnails=False)


class PreparationError(ValueError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


def _page(image, source, source_index, source_page, number):
    with ExitStack() as stack:
        if image.mode in ("RGBA", "LA") or "transparency" in image.info:
            rgba = stack.enter_context(image.convert("RGBA"))
            white = stack.enter_context(Image.new("RGBA", image.size, "white"))
            image = stack.enter_context(Image.alpha_composite(white, rgba))
        rgb = stack.enter_context(image.convert("RGB"))
        output = BytesIO()
        rgb.save(output, format="PNG")
        return DocumentPage(content=output.getvalue(), number=number, source_name=source.name,
                            source_index=source_index, source_page_number=source_page,
                            width=rgb.width, height=rgb.height)


def _open_pdf(source):
    handle = pdfium.raw.FPDF_LoadMemDocument64(source.content, len(source.content), None)
    if not handle:
        error = pdfium.raw.FPDF_GetLastError()
        code = "password_required" if error == pdfium.raw.FPDF_ERR_PASSWORD else "invalid_file"
        raise PreparationError(code, "Cannot open PDF document")
    return pdfium.PdfDocument(handle)


def _check_count(count, config):
    if config.max_pages is not None and count > config.max_pages:
        raise PreparationError("too_many_pages", "Document exceeds the page limit")


def _check_pixels(width, height, total, config):
    if width <= 0 or height <= 0:
        raise PreparationError("invalid_file", "Document page has invalid dimensions")
    pixels = width * height
    if config.max_page_pixels is not None and pixels > config.max_page_pixels:
        raise PreparationError("image_too_large", "Page exceeds the pixel limit")
    total += pixels
    if config.max_document_pixels is not None and total > config.max_document_pixels:
        raise PreparationError("image_too_large", "Document exceeds the pixel limit")
    return total


def prepare_document(sources: Sequence[SourceFile], config: PreparationConfig | None = None):
    config = config if config is not None else PreparationConfig()
    originals = tuple(sources)
    if not originals:
        raise PreparationError("empty_document", "At least one source file is required")
    # Inspect every page before any image decode or PDF rendering. Source handles
    # stay alive until rendering finishes, so TIFF frames can be revisited safely.
    pages, descriptors = [], []
    pixels = 0
    with ExitStack() as stack:
        for index, source in enumerate(originals, 1):
            if not source.content:
                raise PreparationError("empty_file", "Files must not be empty")
            try:
                if is_pdf(source.content):
                    pdf = stack.enter_context(_open_pdf(source))
                    count = len(pdf)
                    if not count:
                        raise PreparationError("empty_document", "PDF has no pages")
                    _check_count(len(descriptors) + count, config)
                    for page_index in range(count):
                        with closing(pdf[page_index]) as page:
                            width, height = page.get_size()
                        scale = config.pdf_dpi / 72
                        if not all(math.isfinite(value) and value > 0 for value in (width, height)):
                            raise PreparationError("invalid_file", "PDF page has invalid dimensions")
                        pixels = _check_pixels(math.ceil(width * scale), math.ceil(height * scale), pixels, config)
                        descriptors.append((source, index, page_index, page_index + 1, pdf, True))
                else:
                    image = stack.enter_context(Image.open(BytesIO(source.content)))
                    if image.format not in IMAGE_FORMATS:
                        raise PreparationError("unsupported_format", "Unsupported file format")
                    if image.format in ("PNG", "WEBP") and getattr(image, "is_animated", False):
                        raise PreparationError("unsupported_format", "Animated images are not supported")
                    count = image.n_frames if image.format == "TIFF" else 1
                    primary_frame = image.tell()
                    _check_count(len(descriptors) + count, config)
                    for page_index in range(count):
                        frame = page_index if image.format == "TIFF" else primary_frame
                        image.seek(frame)
                        pixels = _check_pixels(*image.size, pixels, config)
                        descriptors.append((source, index, frame, page_index + 1, image, False))
            except PreparationError:
                raise
            except Image.DecompressionBombError as exc:
                raise PreparationError("image_too_large", "Image exceeds decoder pixel limit") from exc
            except UnidentifiedImageError as exc:
                code = "invalid_file" if has_image_signature(source.content) else "unsupported_format"
                raise PreparationError(code, "Cannot identify input document") from exc
            except Exception as exc:
                raise PreparationError("invalid_file", "Cannot inspect input document") from exc
        for source, index, page_index, source_page, document, pdf in descriptors:
            try:
                if pdf:
                    with closing(document[page_index]) as page:
                        with closing(page.render(scale=config.pdf_dpi / 72)) as bitmap:
                            with bitmap.to_pil() as image:
                                pages.append(_page(image, source, index, source_page, len(pages) + 1))
                else:
                    document.seek(page_index)
                    with ImageOps.exif_transpose(document) as image:
                        pages.append(_page(image, source, index, source_page, len(pages) + 1))
            except PreparationError:
                raise
            except Exception as exc:
                raise PreparationError("invalid_file", "Cannot decode input document") from exc
    return PreparedDocument(sources=originals, pages=tuple(pages))


class PrepareDocumentStep:
    input_type = SourceFiles
    output_type = PreparedDocument
    version = "2"

    def __init__(self, pdf_dpi=200, *, max_pages=None, max_page_pixels=None, max_document_pixels=None):
        self.settings = PreparationConfig(pdf_dpi, max_pages, max_page_pixels, max_document_pixels)
        self.config = {"pdf_dpi": pdf_dpi, "max_pages": max_pages,
                       "max_page_pixels": max_page_pixels, "max_document_pixels": max_document_pixels}

    def run(self, input):
        return StepResult(prepare_document(input.files, self.settings))
