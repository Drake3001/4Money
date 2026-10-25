"""Run from VS Code; configure the workflow below, without command-line arguments."""

from pathlib import Path

from experiments.evaluation import write_evaluation
from experiments.parsing import write_parser_evaluation
from extraction.pipeline import Pipeline
from extraction.runner import run_batch
from extraction.steps.docscan import DocscanStep
from extraction.steps.prepare import PrepareDocumentStep
from extraction.steps.orientation import OrientationStep
from extraction.steps.ocr import RapidOCRStep
from extraction.steps.parsing import ParseReceiptStep
from extraction.steps.validate import ValidateReceiptStep

WORKER_DIR = Path(__file__).resolve().parent
INPUT_DIR = WORKER_DIR / "data" / "input"
OUTPUT_DIR = WORKER_DIR / "data" / "output"
PDF_DPI = 200
DEBUG = True
EXPERIMENT = None  # Or "preprocessing" / "ocr_comparison" / "gemini".
GEMINI_OCR_DIR = None  # None selects the newest saved holdout run; or set a Path.
GEMINI_MODEL = "gemini-3.1-flash-lite"
GEMINI_LIMIT = 10
GEMINI_EXECUTE = False  # False prepares requests without API calls.
GEMINI_MAX_COST_USD = "1.00"  # Conservative preflight reservation at standard list rates.
OCR_VARIANTS = ("rapidocr", "paddle_mobile", "paddle_server_det", "easyocr", "tesseract")
OCR_REPEATS = 1
LOCAL_TESSERACT = WORKER_DIR / ".cache" / "tools" / "tesseract" / "tesseract.exe"
TESSERACT_COMMAND = str(LOCAL_TESSERACT) if LOCAL_TESSERACT.is_file() else "tesseract"
TESSDATA_DIR = WORKER_DIR / ".cache" / "models" / "tesseract"


def build_pipeline(*, enricher=None, vision_client=None):
    """Default local workflow; API clients must be supplied explicitly by the caller."""
    if enricher is not None and vision_client is not None:
        raise ValueError("Choose either text enrichment or image-only analysis")
    if vision_client is not None:
        from extraction.workflows import vision_analysis_pipeline
        return vision_analysis_pipeline(vision_client)
    prefix = [
        ("prepare", PrepareDocumentStep(pdf_dpi=PDF_DPI)),
        # ("docscan", DocscanStep()),
        ("orient", OrientationStep()),
        ("ocr", RapidOCRStep()),
    ]
    if enricher is not None:
        from extraction.workflows import text_analysis_pipeline
        return text_analysis_pipeline(enricher, prefix=prefix)
    return Pipeline([*prefix, ("parse", ParseReceiptStep()), ("validate", ValidateReceiptStep())])


def main():
    try:
        INPUT_DIR.mkdir(parents=True, exist_ok=True)
        if EXPERIMENT == "ocr_comparison":
            from experiments.ocr_comparison import run_ocr_comparison

            root, failed = run_ocr_comparison(
                INPUT_DIR, OUTPUT_DIR, dpi=PDF_DPI, debug=DEBUG,
                names=OCR_VARIANTS, repeats=OCR_REPEATS,
                tesseract_command=TESSERACT_COMMAND, tessdata_dir=TESSDATA_DIR,
            )
        elif EXPERIMENT == "gemini":
            from experiments.gemini import run_failed, run_gemini

            ocr_dir = GEMINI_OCR_DIR
            if ocr_dir is None:
                candidates = [path for path in OUTPUT_DIR.iterdir()
                              if path.is_dir() and (path / "holdout-manifest.json").is_file()]
                ocr_dir = max(candidates, key=lambda path: path.stat().st_mtime, default=None)
            if ocr_dir is None:
                raise ValueError("Set GEMINI_OCR_DIR to an existing saved OCR run")
            root = run_gemini(ocr_dir, INPUT_DIR, OUTPUT_DIR, model=GEMINI_MODEL,
                              limit=GEMINI_LIMIT, execute=GEMINI_EXECUTE,
                              max_cost_usd=GEMINI_MAX_COST_USD)
            failed = run_failed(root)
        elif EXPERIMENT == "preprocessing":
            from experiments.preprocessing import run_preprocessing

            root, variants = run_preprocessing(INPUT_DIR, OUTPUT_DIR, PDF_DPI, DEBUG)
            failed = any(
                not result.ok
                for cases in variants.values()
                for result in cases.values()
            )
        elif EXPERIMENT is None:
            root, results = run_batch(build_pipeline(), INPUT_DIR, OUTPUT_DIR, DEBUG)
            write_evaluation(root, INPUT_DIR, results)
            write_parser_evaluation(root, INPUT_DIR, results)
            failed = any(not result.ok for result in results.values())
            if not results:
                print(
                    f"Add JPEG, PNG or PDF files, or receipt folders, inside: {INPUT_DIR}"
                )
        else:
            raise ValueError(f"Unknown experiment: {EXPERIMENT!r}")
    except (OSError, ValueError) as exc:
        print(f"Cannot run worker: {exc}")
        return 1
    print(f"Results: {root}")
    return int(failed)


if __name__ == "__main__":
    raise SystemExit(main())
