"""Local CPU OCR baseline. Receipt parsing is deliberately a separate step."""

from importlib.metadata import PackageNotFoundError, version
from pathlib import Path

from ..contracts import (
    ExtractionWarning,
    OCRFragment,
    OCRPage,
    OCRResult,
    PreparedDocument,
    StepResult,
)


class RapidOCRStep:
    input_type = PreparedDocument
    output_type = OCRResult
    name = "rapidocr"

    def __init__(self, model_dir: Path | None = None):
        self.model_dir = (
            model_dir
            if model_dir is not None
            else Path(__file__).resolve().parents[2] / ".cache" / "models" / "rapidocr"
        )
        self.config = {
            "detector": "PP-OCRv5-mobile",
            "recognizer": "PP-OCRv5-mobile-latin",
            "classifier": "PP-OCRv4-mobile",
            "device": "cpu",
            "text_score": 0.5,
            "max_side_len": 4096,
            "det_limit_side_len": 2048,
            "intra_op_num_threads": 4,
            "inter_op_num_threads": 1,
            "model_dir": str(self.model_dir),
        }
        self._engine = None
        try:
            self.version = f"{version('rapidocr')};onnxruntime={version('onnxruntime')};PP-OCRv5-mobile-latin"
        except PackageNotFoundError:
            self.version = "not-installed;PP-OCRv5-mobile-latin"

    def _get_engine(self):
        if self._engine is None:
            try:
                from rapidocr import (
                    EngineType,
                    LangDet,
                    LangRec,
                    ModelType,
                    OCRVersion,
                    RapidOCR,
                )
            except ImportError as exc:
                raise RuntimeError(
                    "OCR dependencies are missing; install the ocr extra from pyproject.toml"
                ) from exc
            self._engine = RapidOCR(
                params={
                    "Global.model_root_dir": str(self.model_dir),
                    "Global.log_level": "warning",
                    "Global.max_side_len": 4096,
                    "Global.text_score": 0.5,
                    "EngineConfig.onnxruntime.intra_op_num_threads": 4,
                    "EngineConfig.onnxruntime.inter_op_num_threads": 1,
                    "Det.engine_type": EngineType.ONNXRUNTIME,
                    "Det.lang_type": LangDet.CH,
                    "Det.model_type": ModelType.MOBILE,
                    "Det.ocr_version": OCRVersion.PPOCRV5,
                    "Det.limit_side_len": 2048,
                    "Det.limit_type": "max",
                    "Det.mean": [0.485, 0.456, 0.406],
                    "Det.std": [0.229, 0.224, 0.225],
                    "Cls.engine_type": EngineType.ONNXRUNTIME,
                    "Cls.model_type": ModelType.MOBILE,
                    "Cls.ocr_version": OCRVersion.PPOCRV4,
                    "Rec.engine_type": EngineType.ONNXRUNTIME,
                    "Rec.lang_type": LangRec.LATIN,
                    "Rec.model_type": ModelType.MOBILE,
                    "Rec.ocr_version": OCRVersion.PPOCRV5,
                }
            )
        return self._engine

    def run(self, document: PreparedDocument) -> StepResult:
        engine = self._get_engine()
        pages = []
        warnings = [
            ExtractionWarning(
                "ocr_only",
                "Only OCR was performed; receipt fields and products have not been parsed",
            )
        ]
        for page in document.pages:
            output = engine(page.content)
            texts = output.txts if output.txts is not None else ()
            scores = output.scores if output.scores is not None else ()
            boxes = output.boxes.tolist() if output.boxes is not None else []
            lines = [
                {"text": text, "confidence": float(score), "box": box}
                for text, score, box in zip(texts, scores, boxes, strict=True)
            ]
            if not lines:
                warnings.append(
                    ExtractionWarning(
                        "no_text", f"No text detected on page {page.number}"
                    )
                )
            elif any(line["confidence"] < 0.8 for line in lines):
                warnings.append(
                    ExtractionWarning(
                        "low_confidence",
                        f"Page {page.number} contains OCR fragments with confidence below 0.8",
                    )
                )
            pages.append(
                OCRPage(
                    page.number,
                    page.source_name,
                    page.source_index,
                    page.source_page_number,
                    page.width,
                    page.height,
                    tuple(
                        OCRFragment(
                            line["text"],
                            line["confidence"],
                            tuple(
                                tuple(float(v) for v in point) for point in line["box"]
                            ),
                        )
                        for line in lines
                    ),
                )
            )
        return StepResult(
            OCRResult(
                document, tuple(pages), self.name, self.version, dict(self.config)
            ),
            tuple(warnings),
        )
