"""Lazy Polish/English EasyOCR adapter, using CPU only."""

from pathlib import Path

from .common import MODEL_ROOT, LocalOCRStep, fragment, package_version


class EasyOCRStep(LocalOCRStep):
    name = "easyocr"

    def __init__(self, model_dir=None):
        self.model_dir = Path(model_dir) if model_dir else MODEL_ROOT / "easyocr"
        self.version = package_version("easyocr", "torch", "torchvision")
        self.config = {
            "languages": ["pl", "en"],
            "detector": "craft",
            "recognizer": "latin_g2",
            "device": "cpu",
            "cpu_threads": 4,
            "model_dir": str(self.model_dir),
            "detail": 1,
            "paragraph": False,
            "decoder": "greedy",
            "batch_size": 1,
            "workers": 0,
            "quantize": True,
        }
        self._engine = None

    def initialize(self):
        if self._engine is None:
            import torch
            from easyocr import Reader

            torch.set_num_threads(4)
            self._engine = Reader(
                ["pl", "en"], gpu=False, detect_network="craft", recog_network="latin_g2",
                model_storage_directory=str(self.model_dir),
                user_network_directory=str(self.model_dir / "user_network"),
                quantize=True, verbose=False,
            )

    def recognize(self, content):
        results = self._engine.readtext(
            content, detail=1, paragraph=False, decoder="greedy", batch_size=1, workers=0,
        )
        return [fragment(text, score, box) for box, text, score in results]
