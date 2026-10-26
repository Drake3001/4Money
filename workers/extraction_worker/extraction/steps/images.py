"""Typed bridges from prepared pages to image analysis inputs."""

from io import BytesIO

from PIL import Image

from ..contracts import ImageOCRInput, OCRResult, PreparedDocument, SourceFile, SourceFiles, StepResult


def page_sources(document, *, optimize_transport=False):
    sources = []
    for page in document.pages:
        content, mime_type, extension = page.content, page.mime_type, "png"
        if optimize_transport:
            buffer = BytesIO()
            with Image.open(BytesIO(page.content)) as image:
                image.convert("RGB").save(buffer, format="JPEG", quality=95, subsampling=0)
            jpeg = buffer.getvalue()
            if len(jpeg) < len(content):
                content, mime_type, extension = jpeg, "image/jpeg", "jpg"
        sources.append(SourceFile(content, mime_type, f"page-{page.number}.{extension}"))
    return SourceFiles(tuple(sources))


class PreparedImagesStep:
    input_type = PreparedDocument
    output_type = SourceFiles
    version = "2"

    def __init__(self, *, optimize_transport=False):
        self.optimize_transport = optimize_transport
        self.config = {"images": "prepared_pages", "transport": "smaller_png_or_jpeg" if optimize_transport else "png",
                       "resize": False}
        if optimize_transport:
            self.config.update(jpeg_quality=95, jpeg_subsampling=0)

    def run(self, input):
        return StepResult(page_sources(input, optimize_transport=self.optimize_transport))


class OCRImagesStep:
    input_type = OCRResult
    output_type = ImageOCRInput
    version = "2"

    def __init__(self, *, optimize_transport=False):
        self.optimize_transport = optimize_transport
        self.config = {**PreparedImagesStep(optimize_transport=optimize_transport).config,
                       "images": "ocr_document_pages"}

    def run(self, input):
        return StepResult(ImageOCRInput(page_sources(input.document, optimize_transport=self.optimize_transport), input))
