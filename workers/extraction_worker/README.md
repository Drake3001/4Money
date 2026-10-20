# Extraction worker

## Purpose

A composable receipt processing pipeline for images and PDF documents.
Each step performs one operation and passes its result to the next step.

```text
Preparation → OCR → Parsing → Validation → Classification (planned)
```

Preparation, OCR and a rule-based receipt parser are implemented. Optional preprocessing steps and local
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
and, after successful OCR, `ocr.json` and `ocr.txt`. The OCR JSON retains text, confidence, four-point
bounding boxes, page dimensions, and source information. Coordinates refer to
the prepared image passed to OCR. Reference annotations are optional.

`result.json` contains the parsed receipt: goods, item discounts, issued deposits,
signed deposit returns, goods total and amount due. Unknown values remain null.
Field evidence points back to OCR fragments and marks observed or derived values.
Validation reports arithmetic inconsistencies without rewriting the receipt.

The parser targets Polish fiscal receipts and uses the `PARAGON FISKALNY` marker
to exclude background text. Missing OCR text stays unknown; names are not corrected
from a product catalogue. A discount continued onto another page is reported as
unassigned rather than silently applied to a product on the previous page.

## Code structure

| Location | Responsibility |
|---|---|
| `main.py` | Workflow selection, settings, and pipeline composition |
| `extraction/pipeline.py` | Sequential execution, timing, and errors |
| `extraction/contracts.py` | Shared input and output types |
| `extraction/steps/` | Preparation, preprocessing, OCR, parsing and validation steps |
| `extraction/steps/ocr/common.py` | Shared OCR page handling and result construction |
| `extraction/steps/ocr/` | Engine initialization and recognition adapters |
| `extraction/runner.py`, `reporting.py` | File input, batch execution, and output |
| `experiments/` | Preprocessing/OCR comparisons, metrics, and reference-guided audits |

OCR adapters share `run()` through `LocalOCRStep` and implement `initialize()`
and `recognize()`. `ParseReceiptStep` receives `OCRResult` and returns `ReceiptData`;
the pipeline serializes it to JSON. Classification remains a separate planned step.

Replay an existing RapidOCR run without reading images or loading OCR models:

```sh
uv run python -m experiments.parsing data/output/<run>/rapidocr data/input data/output/<new-parser-run>
```

The destination must be new. `parsing-evaluation/report.json` and `report.md`
compare predictions with annotations, including missing/excess products and exact
numeric fields. Approximate names and unreviewed references are identified separately.

## Gemini parser benchmark

The optional benchmark compares Gemini with the rule parser on the same saved OCR.
It sends OCR text, confidence and normalized boxes to Google, without photos,
filenames, reference annotations or rule predictions. Both parsers return `ReceiptData`.
No extra dependencies are needed; the default pipeline still uses rules.

`GeminiParseStep` lives in `extraction/steps/parsing/gemini.py` and can replace
`ParseReceiptStep` in a regular pipeline, followed by the same validation step.
Pass the API key to its constructor; the model defaults to `gemini-3.1-flash-lite`.
Caching is optional via `cache_dir`. Only experiment orchestration, local key
configuration and cost/comparison reporting live in `experiments/gemini.py`.

Copy `.env.example` to `.env` in this worker directory and fill `GEMINI_API_KEY`
locally. The file is ignored by Git. Never paste the key into chat.
In `main.py`, set `EXPERIMENT = "gemini"`. Initially leave
`GEMINI_EXECUTE = False`: running `uv run main.py` prepares ten evenly spaced
cases from the newest saved holdout run, without accessing the key or API.
Set `GEMINI_OCR_DIR` explicitly to use another saved OCR directory.

Inspect `request.json` and `gemini-run.json` in the new output directory, then set
`GEMINI_EXECUTE = True` to make requests. The default model is
`gemini-3.1-flash-lite`. `GEMINI_MAX_COST_USD` checks a conservative token reservation
before execution; it is an estimate, not a provider billing cap. Actual usage and
estimated cost are recorded per case. Responses are cached under `.cache/gemini/`;
identical requests reuse them. API/network errors stop the remaining batch.

`comparison.md` and `comparison.json` compare both parsers with local annotations;
unreviewed references and heuristic product alignment still limit the conclusions.
Each case retains the request, provider response, parsed result, warnings and
OCR evidence. Missing or unsupported evidence is flagged; a citation alone does
not establish that a price belongs to the right product.

## Team guide

See the [Polish team guide](../../notes/extraction-worker.md) for Windows/Linux
setup, optional engines, configuration, experiments, annotations, and tests.
