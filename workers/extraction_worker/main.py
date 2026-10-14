"""Run from VS Code; configure the workflow below, without command-line arguments."""

from pathlib import Path

from experiments.evaluation import write_evaluation
from extraction.pipeline import Pipeline
from extraction.runner import run_batch
from extraction.steps.docscan import DocscanStep
from extraction.steps.prepare import PrepareDocumentStep
from extraction.steps.rapidocr import RapidOCRStep

WORKER_DIR = Path(__file__).resolve().parent
INPUT_DIR = WORKER_DIR / "data" / "input"
OUTPUT_DIR = WORKER_DIR / "data" / "output"
PDF_DPI = 200
DEBUG = True
EXPERIMENT = None  # Set to "preprocessing" to compare original/docscan/UVDoc.


def build_pipeline():
    return Pipeline(
        [
            ("prepare", PrepareDocumentStep(pdf_dpi=PDF_DPI)),
            # ("docscan", DocscanStep()),
            ("ocr", RapidOCRStep()),
        ]
    )


def main():
    try:
        INPUT_DIR.mkdir(parents=True, exist_ok=True)
        if EXPERIMENT == "preprocessing":
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
