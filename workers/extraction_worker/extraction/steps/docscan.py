"""Optional background removal and perspective correction using docscan."""

import os
from dataclasses import replace
from importlib.metadata import PackageNotFoundError, version
from io import BytesIO
from pathlib import Path

from PIL import Image

from ..contracts import PreparedDocument, StepResult


def transform_pages(document, transform):
    pages = []
    for page in document.pages:
        content = transform(page.content)
        with Image.open(BytesIO(content)) as image:
            with image.convert("RGB") as rgb:
                buffer = BytesIO()
                rgb.save(buffer, format="PNG")
                pages.append(
                    replace(
                        page,
                        content=buffer.getvalue(),
                        width=rgb.width,
                        height=rgb.height,
                    )
                )
    return PreparedDocument(document.sources, tuple(pages))


class DocscanStep:
    input_type = PreparedDocument
    output_type = PreparedDocument
    config = {"alpha_matting": False}

    def __init__(self, model_dir=None):
        self.model_dir = (
            Path(model_dir)
            if model_dir
            else Path(__file__).resolve().parents[2] / ".cache" / "models" / "rembg"
        )
        try:
            self.version = version("docscan")
        except PackageNotFoundError:
            self.version = "not-installed"
        self.config = {
            **self.config,
            "model_dir": str(self.model_dir),
            "model": "u2net",
        }
        self._session = None

    def _transform(self, content):
        os.environ.setdefault("U2NET_HOME", str(self.model_dir))
        os.environ.setdefault("NUMBA_DISABLE_JIT", "1")
        import cv2
        import imutils
        import numpy as np
        from docscan.doc import APPROX_POLY_DP_ACCURACY_RATIO, IMG_RESIZE_H
        from imutils import perspective
        from rembg import new_session, remove

        if self._session is None:
            self._session = new_session("u2net")
        # docscan.scan creates a rembg session for every page and has no session
        # parameter. Keep its algorithm here so one lazy session can be reused.
        processed = remove(content, session=self._session, alpha_matting=False)
        image = cv2.imdecode(np.frombuffer(processed, np.uint8), cv2.IMREAD_UNCHANGED)
        if image is None or image.ndim != 3 or image.shape[2] != 4:
            raise RuntimeError("docscan requires a decoded image with an alpha channel")
        ratio = image.shape[0] / IMG_RESIZE_H
        resized = imutils.resize(image, height=int(IMG_RESIZE_H))
        _, mask = cv2.threshold(resized[:, :, 3], 0, 255, cv2.THRESH_BINARY)
        mask = cv2.medianBlur(mask, 15)
        contours = imutils.grab_contours(
            cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        )
        outline = None
        for contour in sorted(contours, key=cv2.contourArea, reverse=True):
            perimeter = cv2.arcLength(contour, True)
            polygon = cv2.approxPolyDP(
                contour, APPROX_POLY_DP_ACCURACY_RATIO * perimeter, True
            )
            if len(polygon) == 4:
                outline = polygon.reshape(4, 2)
                break
        # Same documented library behavior when corners are absent.
        result = (
            image
            if outline is None
            else perspective.four_point_transform(image, outline * ratio)
        )
        ok, buffer = cv2.imencode(".png", result)
        if not ok:
            raise RuntimeError("docscan could not encode its result")
        return buffer.tobytes()

    def run(self, input):
        return StepResult(transform_pages(input, self._transform))
