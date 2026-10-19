# Extraction worker

## Purpose

A composable receipt processing pipeline for images and PDF documents.
Each step performs one operation and passes its result to the next step.

```text
Preparation → OCR → Parsing (planned) → Classification (planned)
```

Preparation and OCR are implemented. Optional preprocessing steps and local
experiments support comparing image processing methods and OCR engines.

## Quick start

Install [uv](https://docs.astral.sh/uv/getting-started/installation/) 0.12.23 or newer.
The following commands work in Windows PowerShell and Linux shells:

```sh
cd workers/extraction_worker
uv sync
uv run main.py
```

uv selects Python 3.12, creates `.venv`, and installs the default RapidOCR pipeline
from `uv.lock`. Missing Python installations and models may be downloaded.
Keep `EXPERIMENT = None` in `main.py` for the default workflow.

## Input and output

Place JPEG, PNG, or PDF files in `data/input/`; create the directory if needed.
A direct subdirectory groups multiple files into one receipt, ordered by filename.
Run `uv run main.py` after adding documents.

Each run creates `data/output/<run>/`. Document folders contain `result.json`
and, after successful OCR, `ocr.txt`. JSON retains text, confidence, four-point
bounding boxes, page dimensions, and source information. Coordinates refer to
the prepared image passed to OCR. Reference annotations are optional.

## Code structure

| Location | Responsibility |
|---|---|
| `main.py` | Workflow selection, settings, and pipeline composition |
| `extraction/pipeline.py` | Sequential execution, timing, and errors |
| `extraction/contracts.py` | Shared input and output types |
| `extraction/steps/` | Preparation, preprocessing, and OCR steps |
| `extraction/steps/ocr/common.py` | Shared OCR page handling and result construction |
| `extraction/steps/ocr/` | Engine initialization and recognition adapters |
| `extraction/runner.py`, `reporting.py` | File input, batch execution, and output |
| `experiments/` | Preprocessing/OCR comparisons, metrics, and reference-guided audits |

OCR adapters share `run()` through `LocalOCRStep` and implement `initialize()`
and `recognize()`. Parsing and classification will be separate pipeline steps.

## Team guide

See the [Polish team guide](../../notes/extraction-worker.md) for Windows/Linux
setup, optional engines, configuration, experiments, annotations, and tests.
