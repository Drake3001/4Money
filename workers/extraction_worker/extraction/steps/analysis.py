"""Composable text/vision analysis steps with one typed output contract."""

import copy
import re

from ..categories import TAXONOMY_VERSION
from ..contracts import (
    AnalyzedReceipt, ImageEvidence, ImageOCRInput, ItemAnnotation, OCRResult, ParsedReceipt,
    ReceiptData, SourceFiles, StepResult,
)
from ..prompts import prompt_metadata
from .parsing import ParseReceiptStep
from .parsing.vision import OUTPUT_TOKENS, PAID_MODEL


def receipt_chunk(receipt, start, stop):
    """Isolate items and remap provenance without changing the source receipt."""
    chunk = copy.deepcopy(receipt)
    chunk.items = chunk.items[start:stop]
    chunk.evidence = {}
    for path, evidence in receipt.evidence.items():
        match = re.match(r"items\[(\d+)\](.*)", path)
        if match is None:
            chunk.evidence[path] = evidence
        elif start <= int(match[1]) < stop:
            chunk.evidence[f"items[{int(match[1]) - start}]{match[2]}"] = evidence
    return chunk


def analyzed_receipt(receipt, enrichment, image_evidence=()):
    return AnalyzedReceipt(
        receipt=receipt,
        annotations=tuple(ItemAnnotation(**item) for item in enrichment["items"]),
        image_evidence=tuple(ImageEvidence(claim["field"], tuple(claim["images"])) for claim in image_evidence),
    )


class ParseReceiptContextStep:
    """Retain OCR for enrichment while leaving existing parse-only workflows intact."""

    input_type = OCRResult
    output_type = ParsedReceipt
    version = "1"

    def __init__(self, parser=None):
        self.parser = ParseReceiptStep() if parser is None else parser
        if self.parser.input_type is not OCRResult or self.parser.output_type is not ReceiptData:
            raise TypeError("Context parser must map OCRResult to ReceiptData")
        self.config = {"parser": type(self.parser).__name__, "version": self.parser.version,
                       "config": copy.deepcopy(self.parser.config)}

    def run(self, input):
        result = self.parser.run(input)
        return StepResult(ParsedReceipt(result.output, input), result.warnings)


class ClassifyReceiptItemsStep:
    """Classify items and propose corrected names without changing receipt fields."""

    input_type = ParsedReceipt
    output_type = AnalyzedReceipt
    version = "1"

    def __init__(self, client, *, chunk_size=8):
        if type(chunk_size) is not int or not 1 <= chunk_size <= 8:
            raise ValueError("Enrichment chunk size must be 1–8")
        self.client, self.chunk_size = client, chunk_size
        self.config = {"client": type(client).__name__, "model": client.model,
                       **prompt_metadata(client.prompt, client.prompt_id),
                       "taxonomy_version": TAXONOMY_VERSION, "chunk_size": chunk_size,
                       "max_output_tokens": getattr(client, "max_output_tokens", None),
                       "resilient": getattr(client, "resilient", False)}

    def run(self, input):
        items = []
        for start in range(0, len(input.receipt.items), self.chunk_size):
            part = receipt_chunk(input.receipt, start, start + self.chunk_size)
            result = self.client.run(part, input.ocr)
            items.extend({**item, "id": item["id"] + start} for item in result["items"])
        return StepResult(analyzed_receipt(input.receipt, {"items": items}))


class AnalyzeReceiptImagesStep:
    """Extract receipt fields and classify products from original images."""

    input_type = SourceFiles
    output_type = AnalyzedReceipt
    version = "1"

    def __init__(self, client):
        self.client = client
        self.config = {"client": type(client).__name__, "model": PAID_MODEL,
                       **prompt_metadata(client.prompt, client.prompt_id),
                       "taxonomy_version": TAXONOMY_VERSION, "max_output_tokens": OUTPUT_TOKENS,
                       "input": "original_images"}

    def run(self, input):
        result = self.client.run(input.files)
        return StepResult(analyzed_receipt(result.output, self.client.enrichment,
                                           self.client.image_evidence), result.warnings)


class AnalyzeReceiptImagesWithOCRStep(AnalyzeReceiptImagesStep):
    """Analyze images with fallible OCR as supporting document data."""

    input_type = ImageOCRInput

    def __init__(self, client):
        super().__init__(client)
        self.config.update(input="images_and_ocr",
                           **prompt_metadata(client.ocr_prompt, client.ocr_prompt_id))
        guidance = prompt_metadata(client.ocr_guidance, client.ocr_guidance_id)
        self.config.update(ocr_guidance_id=guidance["prompt_id"],
                           ocr_guidance_sha256=guidance["prompt_sha256"])

    def run(self, input):
        result = self.client.run(input.sources.files, ocr=input.ocr)
        return StepResult(analyzed_receipt(result.output, self.client.enrichment,
                                           self.client.image_evidence), result.warnings)
