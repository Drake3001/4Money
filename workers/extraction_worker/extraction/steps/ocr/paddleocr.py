"""Explicit PP-OCRv5 models on the native Paddle CPU runtime."""

import os
from io import BytesIO
from pathlib import Path

from PIL import Image

from .common import MODEL_ROOT, LocalOCRStep, fragment, package_version


class PaddleOCRStep(LocalOCRStep):
    def __init__(self, detector="PP-OCRv5_mobile_det", model_dir=None):
        if detector not in ("PP-OCRv5_mobile_det", "PP-OCRv5_server_det"):
            raise ValueError("Unsupported comparison detector")
        self.name = "paddle_mobile" if detector == "PP-OCRv5_mobile_det" else "paddle_server_det"
        self.model_dir = Path(model_dir) if model_dir else MODEL_ROOT / "paddlex"
        self.version = package_version("paddleocr", "paddlex", "paddlepaddle")
        self.config = {
            "text_detection_model_name": detector,
            "text_recognition_model_name": "latin_PP-OCRv5_mobile_rec",
            "ocr_version": "PP-OCRv5",
            "device": "cpu",
            "cpu_threads": 4,
            "use_doc_orientation_classify": False,
            "use_doc_unwarping": False,
            "use_textline_orientation": False,
            "text_det_limit_side_len": 2048,
            "text_det_limit_type": "max",
            "text_rec_score_thresh": 0.5,
            "text_recognition_batch_size": 1,
            # Paddle 3.3.1 on Windows fails on these PIR models with oneDNN.
            "enable_mkldnn": False,
            "model_dir": str(self.model_dir),
            "engine": "paddle_static",
        }
        self._engine = None

    def initialize(self):
        if self._engine is None:
            os.environ["PADDLE_PDX_CACHE_HOME"] = str(self.model_dir)
            from paddleocr import PaddleOCR

            options = {key: value for key, value in self.config.items() if key != "model_dir"}
            self._engine = PaddleOCR(**options)

    def recognize(self, content):
        import numpy as np

        with Image.open(BytesIO(content)) as image:
            bgr = np.array(image.convert("RGB"))[:, :, ::-1].copy()
        results = list(self._engine.predict(bgr))
        if len(results) != 1:
            raise RuntimeError("PaddleOCR must return one result per page")
        result = results[0]
        texts, scores = result.get("rec_texts", []), result.get("rec_scores", [])
        boxes = result.get("rec_polys")
        if boxes is None:
            boxes = [
                ((x1, y1), (x2, y1), (x2, y2), (x1, y2))
                for x1, y1, x2, y2 in result.get("rec_boxes", [])
            ]
        return [
            fragment(text, score, box)
            for text, score, box in zip(texts, scores, boxes, strict=True)
        ]
