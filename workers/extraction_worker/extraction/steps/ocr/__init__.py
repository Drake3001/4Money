"""Local OCR adapters. Importing this package does not load optional engines."""

from .paddleocr import PaddleOCRStep
from .rapidocr import RapidOCRStep

__all__ = ["RapidOCRStep", "PaddleOCRStep"]
