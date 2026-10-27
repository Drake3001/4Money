"""Whole-page quarter turns using OnnxTR's ready-made orientation predictor."""

import math
from dataclasses import replace
from importlib.metadata import PackageNotFoundError, version
from io import BytesIO
from pathlib import Path

from PIL import Image

from ..contracts import ExtractionWarning, PreparedDocument, StepResult


class OrientationStep:
    input_type = PreparedDocument
    output_type = PreparedDocument

    def __init__(self, model_dir=None, confidence_threshold=0.8):
        if not math.isfinite(confidence_threshold) or not 0 <= confidence_threshold <= 1:
            raise ValueError("confidence_threshold must be between zero and one")
        self.model_dir = (
            Path(model_dir) if model_dir is not None
            else Path(__file__).resolve().parents[2] / ".cache" / "onnxtr"
        )
        self.config = {
            "model": "mobilenet_v3_small_page_orientation",
            "device": "cpu",
            "confidence_threshold": confidence_threshold,
            "intra_op_num_threads": 4,
            "inter_op_num_threads": 1,
            "model_dir": str(self.model_dir),
        }
        try:
            self.version = f"2;onnxtr={version('onnxtr')};onnxruntime={version('onnxruntime')}"
        except PackageNotFoundError:
            self.version = "2;not-installed"
        self._engine = None

    def initialize(self):
        if self._engine is not None:
            return
        import onnxruntime as ort
        from onnxtr.models import (
            EngineConfig,
            mobilenet_v3_small_page_orientation,
            page_orientation_predictor,
        )

        options = ort.SessionOptions()
        options.intra_op_num_threads = self.config["intra_op_num_threads"]
        options.inter_op_num_threads = self.config["inter_op_num_threads"]
        options.log_severity_level = 3
        model = mobilenet_v3_small_page_orientation(
            cache_dir=str(self.model_dir),
            engine_cfg=EngineConfig(providers=["CPUExecutionProvider"], session_options=options),
        )
        self._engine = page_orientation_predictor(model)

    def _predict(self, image):
        import numpy as np

        self.initialize()
        # OnnxTR handles whole-image resize, padding, normalization and inference.
        _, angles, confidences = self._engine([np.asarray(image)])
        angle, confidence = int(angles[0]) % 360, float(confidences[0])
        if angle not in (0, 90, 180, 270) or not math.isfinite(confidence) or not 0 <= confidence <= 1:
            raise ValueError("Invalid OnnxTR orientation prediction")
        return angle, confidence

    def run(self, input):
        pages, warnings = [], []
        for page in input.pages:
            with Image.open(BytesIO(page.content)) as image:
                with image.convert("RGB") as rgb:
                    angle, confidence = self._predict(rgb)
                    if confidence < self.config["confidence_threshold"]:
                        warnings.append(ExtractionWarning(
                            "orientation_uncertain",
                            f"Page {page.number}: orientation confidence {confidence:.3f} "
                            f"below threshold; predicted correction {angle} degrees, image unchanged",
                        ))
                        pages.append(page)
                    elif angle == 0:
                        pages.append(page)
                    else:
                        method = {
                            90: Image.Transpose.ROTATE_90,
                            180: Image.Transpose.ROTATE_180,
                            270: Image.Transpose.ROTATE_270,
                        }[angle]
                        with rgb.transpose(method) as rotated:
                            buffer = BytesIO()
                            rotated.save(buffer, format="PNG", compress_level=1)
                            pages.append(replace(
                                page, content=buffer.getvalue(),
                                width=rotated.width, height=rotated.height,
                            ))
        return StepResult(PreparedDocument(input.sources, tuple(pages)), tuple(warnings))
