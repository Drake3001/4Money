"""Local OCR adapters. Importing this package does not load optional engines."""

from .easyocr import EasyOCRStep
from .paddleocr import PaddleOCRStep
from .rapidocr import RapidOCRStep
from .tesseract import TesseractStep

__all__ = ["RapidOCRStep", "PaddleOCRStep", "EasyOCRStep", "TesseractStep"]
