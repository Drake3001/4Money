"""Typed bridges from prepared pages to image analysis inputs."""

from ..contracts import ImageOCRInput, OCRResult, PreparedDocument, SourceFile, SourceFiles, StepResult


def page_sources(document):
    return SourceFiles(tuple(
        SourceFile(page.content, page.mime_type, f"page-{page.number}.png")
        for page in document.pages
    ))


class PreparedImagesStep:
    input_type = PreparedDocument
    output_type = SourceFiles
    version = "1"
    config = {"images": "prepared_pages"}

    def run(self, input):
        return StepResult(page_sources(input))


class OCRImagesStep:
    input_type = OCRResult
    output_type = ImageOCRInput
    version = "1"
    config = {"images": "ocr_document_pages"}

    def run(self, input):
        return StepResult(ImageOCRInput(page_sources(input.document), input))
