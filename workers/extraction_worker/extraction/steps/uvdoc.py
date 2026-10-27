"""Lazy CPU UVDoc unwarping, with independent local image buffers."""

import os
from importlib.metadata import PackageNotFoundError, version
from io import BytesIO
from pathlib import Path

from PIL import Image

from ..contracts import PreparedDocument, StepResult
from .docscan import transform_pages


class UVDocStep:
    input_type = PreparedDocument
    output_type = PreparedDocument

    def __init__(self, model_dir=None):
        self.model_dir = (
            Path(model_dir)
            if model_dir
            else Path(__file__).resolve().parents[2] / ".cache" / "models" / "paddlex"
        )
        try:
            self.version = version("paddleocr")
        except PackageNotFoundError:
            self.version = "not-installed"
        self.config = {
            "model": "UVDoc",
            "device": "cpu",
            "model_dir": str(self.model_dir),
        }
        self._unwarper = None

    def _transform(self, content):
        import numpy as np

        if self._unwarper is None:
            os.environ.setdefault("PADDLE_PDX_CACHE_HOME", str(self.model_dir))
            from paddleocr import TextImageUnwarping

            self._unwarper = TextImageUnwarping(model_name="UVDoc", device="cpu")
        with Image.open(BytesIO(content)) as image:
            bgr = np.array(image.convert("RGB"))[:, :, ::-1].copy()
        results = list(self._unwarper.predict(bgr))
        if len(results) != 1:
            raise RuntimeError("UVDoc must return one image per page")
        # Input is BGR; doctr_img is already RGB. Never reverse output channels.
        output = BytesIO()
        Image.fromarray(results[0]["doctr_img"]).save(output, format="PNG")
        return output.getvalue()

    def run(self, input):
        return StepResult(transform_pages(input, self._transform))
