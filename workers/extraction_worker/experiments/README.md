# Worker experiments

Run commands from `workers/extraction_worker` after `uv sync`.
Input documents, generated results and caches live in `data/input/`, `data/output/`
and `.cache/`. Reference fixtures and experiment prompts live in `experiments/`.

## Local batch processing

Place JPEG, PNG or PDF files in `data/input/`. A subdirectory groups fragments of
one receipt, ordered by filename. With `EXPERIMENT = None` in `main.py`, run:

```sh
uv run main.py
```

The local batch pipeline runs preparation, orientation, RapidOCR, rule parsing and
validation. Each run creates a directory under `data/output/`, with per-document
`result.json`, `ocr.json` and `ocr.txt`. `DEBUG = True` also saves step artifacts.

Replay parsing against saved OCR and references, using a new output directory:

```sh
uv run python -m experiments.parsing data/output/<run>/rapidocr data/input data/output/<parser-run>
```

## Image preprocessing and OCR engines

Select `EXPERIMENT` in `main.py` and run `uv run main.py`:

| Value | Purpose | Installation |
|---|---|---|
| `preprocessing` | Compare image preprocessing variants | `uv sync --extra experiment` |
| `ocr_comparison` | Compare engines in `OCR_VARIANTS` | Install extras for the selected engines below |

RapidOCR is included by default. Additional engines use `ocr-paddle` or `ocr-easyocr`
extras, for example `uv sync --extra ocr-easyocr`. Tesseract uses a local executable
and language data configured in `main.py`. `ocr-comparison` adds optional fuzzy metrics.

## Provider keys and models

Copy the worker's `.env.example` to `.env` and fill in the keys:

| Variable | Used by |
|---|---|
| `OPENROUTER_API_KEY` | OpenRouter experiments |
| `GEMINI_API_KEY` | Direct Google parsing and Gemma experiments |
| `NVIDIA_API_KEY` | NVIDIA-hosted Gemma experiments |
| `OPENROUTER_MODEL` | Model-selectable OpenRouter experiments |
| `OPENROUTER_BASE_URL` | OpenRouter endpoint; comparisons require the official URL |

The example selects `google/gemini-3.1-flash-lite`. Gemma-specific scripts use their
own model presets; `experiments.gemma` with OpenRouter requires setting
`OPENROUTER_MODEL=google/gemma-4-31b-it:free` in `.env` or the process environment.
Comparison model groups are defined in `model_comparison.py`.

## Parsing and classification benchmarks

Use `uv run python -m experiments.<module> --help` for command-line options.
Commands below prepare requests; add `--execute` to run provider calls.

| Module | Purpose | Example arguments |
|---|---|---|
| `gemini` | Compare Google parsing with rule parsing on saved OCR | `data/output/<ocr-run> --limit 10` |
| `gemma` | Classify synthetic names or one parsed receipt | `--ocr data/output/<run>/<document>/ocr.json` |
| `gemma_batch` | Small paid/free classification comparison | `--help` |
| `gemma_benchmark` | Extended classification benchmark | `--budget 0.50` |
| `gemma_vision` | Compare image extraction with saved text results | `--baseline-run data/output/<baseline-run>` |
| `gemma_multimodal` | Compare images with images plus OCR | `--baseline-run data/output/<baseline-run>` |
| `gemma_hard` | Compare text and image-plus-OCR on hard receipts | `--ocr-dir data/output/<ocr-run>` |

For direct Google parsing:

```sh
uv run python -m experiments.gemini data/output/<ocr-run> --limit 10
```

Alternatively set `EXPERIMENT = "gemini"` in `main.py`, choose `GEMINI_OCR_DIR`,
and set `GEMINI_EXECUTE = True` when ready to make provider calls.

## Latency and model comparisons

| Module | Measures |
|---|---|
| `latency` | Routing, OCR prompt variants, streaming latency and provider metadata |
| `ocr_latency` | Local OCR performance at different CPU thread counts |
| `image_latency` | PNG, JPEG 95, image resizing and compact output |
| `model_comparison` | Speed, receipt fields and categories across model groups |

`latency` prepares three development receipts listed in its `SOURCES` constant;
place those images in `data/input/`. It saves prepared pages, OCR and references:

```sh
uv run python -m experiments.latency --root data/output/latency-study --prepare
uv run python -m experiments.latency --root data/output/latency-study --execute
uv run python -m experiments.latency --root data/output/latency-study --summarize
```

Image comparisons reuse that snapshot:

```sh
uv run python -m experiments.image_latency --root data/output/image-study --previous data/output/latency-study --prepare
uv run python -m experiments.image_latency --root data/output/image-study --execute
uv run python -m experiments.image_latency --root data/output/image-study --summarize
```

For model comparisons, create a new output directory and save the OpenRouter
`GET https://openrouter.ai/api/v1/models` response as `catalog.json` inside it.
Then select `--group original` or `--group budget`, keeping the same group for all phases:

```sh
uv run python -m experiments.model_comparison --root data/output/model-study --previous data/output/latency-study --group budget --prepare
uv run python -m experiments.model_comparison --root data/output/model-study --group budget --execute
uv run python -m experiments.model_comparison --root data/output/model-study --group budget --summarize
```

Use `--variants` to execute selected planned models. `--reconcile` reads billing
metadata for recorded attempts and runs separately from execution.

Local OCR thread profiling reuses a latency snapshot:

```sh
uv run python -m experiments.ocr_latency --root data/output/latency-study --threads 4 2 1 --repeats 2
```

Remote studies record attempts and conservative cost reservations in their output
directories. Failed attempts remain counted; unresolved costs retain their reservation.
Offline `--summarize` rebuilds reports from saved artifacts. These small development
comparisons help select configurations; broader accuracy evaluation needs separate receipts.
