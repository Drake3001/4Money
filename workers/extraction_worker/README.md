# Extraction worker

## Purpose

A composable receipt processing pipeline for images and PDF documents.
Each step performs one operation and passes its result to the next step.

```text
Preparation → Orientation → OCR → Parsing → Validation → Classification (planned)
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

`OrientationStep` corrects whole-page quarter turns before OCR, without cropping,
deskewing or perspective correction. Preparation first applies image EXIF orientation.
The orientation step wraps OnnxTR's `mobilenet_v3_small_page_orientation` on CPU.
OnnxTR prepares the whole image with resizing and padding, and downloads and verifies
its model once (~6 MB) under `.cache/onnxtr/models/`. The step reuses one lazy predictor
for the batch; no Paddle or PyTorch runtime is needed. The CPU headless dependency
supports server deployments on Linux as well as local Windows runs.
Predictions below `confidence_threshold=0.8` leave the page unchanged and add an
`orientation_uncertain` warning. Photos with large backgrounds can remain uncertain.
Unchanged pages retain their PNG bytes; corrected pages retain every pixel and source
identifier. With `DEBUG = True`, corrected images are saved under `debug/orient/`.

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

The parser targets Polish receipts. It tolerates small OCR errors in the fiscal
heading and can infer the purchase section from seller details, purchases and a
currency total. Transaction confirmations with net/tax/gross columns are also
supported. A reading view handles quarter-turn rotations and local text slopes;
original OCR boxes and evidence indices remain unchanged.

Amounts are associated with numeric purchase baselines and discount labels.
An unsigned discount needs confirmation from the printed gross and net amounts;
missing numbers remain unknown. Damaged multipliers can yield partial purchases
without inventing quantity or unit price. Names are not corrected from a catalogue.
A discount continued onto another page is reported as unassigned rather than
silently applied to a product on the previous page. Overlapping receipt photos
are still processed separately and can duplicate purchases.

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

## Free Gemma name correction and classification

This opt-in experiment runs **after the rule parser**. It keeps corrections and
categories in a separate `enrichment.json`; parser names, amounts, item counts and
evidence are not overwritten. The agreed 18-category taxonomy v1 lives in
`extraction/categories.py`; definitions and boundary rules are injected into
every provider's prompt. No photos are sent: the request contains product names,
cited OCR text for those names, and merchant text/context from the parser.

See [category and name-correction policy](../../notes/receipt-categories.md).
Every predicted item now requires `needs_review` (boolean) and `review_reason`
(null or a controlled reason). Uncertain categories are null, never `inne` or
an extra `nieustalone` category. Missing names cannot be invented. A correction
that changes standalone readable numbers such as a weight is replaced by the
raw name and flagged `unsupported_name_change`. This protects numbers, not
all possible semantic mistakes. Parser amounts and quantities remain untouched.
Prompt changes naturally invalidate response cache keys; historical responses
are not silently accepted under the new output contract.

Re-evaluate the saved initial paid run without reading credentials or calling API:

```sh
uv run python -m experiments.gemma_audit "data/output/2026-10-24_194228_68s3udt8"
```

The bundled review fixture was prepared by the assistant after inspecting the
three source images, not independently approved by a human. The audit preserves
the original run and explicitly maps old category names to their v1 equivalents;
it never fixes wrong model classifications. Its result measures the old prompt
on a development sample, not the new prompt. Labels and review data never enter
model requests. Missing legacy uncertainty fields are recorded as unknown.

Add `OPENROUTER_API_KEY` to the worker's ignored `.env` (see `.env.example`).
The only permitted model is `google/gemma-4-31b-it:free`, using the official
OpenRouter base URL. Requests additionally set a zero price ceiling and never
fall back to a paid model. Free-service quota and availability still apply.

From this worker directory, prepare a synthetic request without calling the API:

```sh
uv run python -m experiments.gemma
```

Test the connection with three synthetic product names:

```sh
uv run python -m experiments.gemma --execute
```

To enrich one real receipt, pass its saved OCR explicitly:

```sh
uv run python -m experiments.gemma --ocr "data/output/<run>/<document>/ocr.json" --execute
```

The experiment does not rerun OCR. It saves the parser output, request,
enrichment and status (provider, reported usage/cost, time, cache hit) under a new
`data/output/<run>/` folder. Valid responses are cached under `.cache/gemma/`.
Invalid/truncated responses fail; there are no automatic network retries.
The default `main.py` pipeline remains local and uses rules.

### Small Gemma benchmark and accuracy review

Paid OpenRouter access requires an explicit opt-in; adding credits alone does
not enable paid calls. Run at most three attempts with a conservative USD 0.10
reservation cap:

```sh
uv run python -m experiments.gemma_batch --provider openrouter --paid --limit 3 --max-calls 3 --max-cost-usd 0.10 --execute
```

This selects `google/gemma-4-31b-it` (without `:free`) regardless of the free
model setting in `.env`. CoreWeave and DeepInfra are preferred, with provider
fallback allowed within this same model and price ceilings of USD 0.30/M input
and USD 0.60/M output. Output is capped at 1024 tokens. The experiment checks a
conservative byte-based input reservation before any request and counts every
attempt against the budget and call cap. This is a local estimate, not a
provider billing guarantee; retain the API key's spending limit. The report
records provider, token usage, API-reported cost and reservation separately;
raw completed responses are retained even when JSON validation fails. Paid
responses have a separate `.cache/gemma-openrouter-paid/` cache. Default mode
remains free-only and does not permit the paid model.

Prepare three cases (six products total), without API requests:

```sh
uv run python -m experiments.gemma_batch
```

Run at most three requests, one per receipt:

```sh
uv run python -m experiments.gemma_batch --limit 3 --execute
```

`--limit` permits only 2–5 receipts. The fixed sample starts with an app
screenshot, a food/household-paper receipt and a beer receipt; cases 4–5 add
difficult OCR and mixed categories. Each case uses the newest available saved
OCR for that source, with its exact path recorded in the report. The first
network or decoding failure stops all remaining requests. Cache hits do not make
API calls. There are no separate smoke calls or automatic retries.

Optional paced continuation, allowing one retry only on HTTP 500/502/503/504:

```sh
uv run python -m experiments.gemma_batch --provider google --limit 3 --max-calls 3 --min-interval 20 --retry-server-error --execute
```

The 20-second interval applies after each completed network attempt, including
failed attempts. It is a conservative pacing choice, not a discovered project
quota. All attempts, including retries, share the hard `--max-calls` cap (1–5,
default: receipt count). Valid cache hits use no attempts and introduce no wait.
HTTP 429 and client errors are not retried. A second server error stops the run.
Each case records attempt statuses; historical successful cache usage is not a
new call. No additional receipts are selected when continuing the same sample.

Alternatively use Google directly, reusing the existing `GEMINI_API_KEY` from
the environment or worker `.env`:

```sh
uv run python -m experiments.gemma_batch --provider google --limit 3 --execute
```

This calls only `gemma-4-31b-it` on Google's `generateContent` endpoint, with
thinking set to minimal. Google's pricing lists Gemma 4 as free (checked
2026-10-23); project quotas and availability apply. It does not select Gemini
models or fall back to OpenRouter. Google responses use a separate cache under
`.cache/gemma-google/`. The same parser inputs, review and request cap apply.
Omit `--execute` to prepare requests without reading the Google key or calling API.
Google reports token counts in `usageMetadata`; absence of a monetary cost field
is not a measured cost value.

NVIDIA's hosted prototype endpoint is also supported. Put `NVIDIA_API_KEY` in
the worker `.env`; existing Google and OpenRouter keys remain separate. Test
one request with three synthetic product names:

```sh
uv run python -m experiments.gemma --provider nvidia --execute
```

To run the same three real receipts through NVIDIA, with at most three attempts:

```sh
uv run python -m experiments.gemma_batch --provider nvidia --limit 3 --max-calls 3 --min-interval 20 --execute
```

Only `google/gemma-4-31b-it` at `https://integrate.api.nvidia.com/v1` is used;
there is no cross-provider/model fallback. NVIDIA labels this a free prototype
endpoint (checked 2026-10-23); availability and service quotas still apply.
Valid responses have their own cache under `.cache/gemma-nvidia/`. The same
output validation and offline review metrics apply. Omitting `--execute` prepares
NVIDIA requests without loading the key or making API calls.

`report.md` shows names before/after, categories, status, actual API attempt count,
and parser completeness against existing references (whose review status is
retained). Those abbreviated printed-name references are **not** treated as
ground truth for semantic name correction or classification.

To measure accuracy, copy `review.template.json` to `review.json`, inspect the
original inputs and fill `accepted_names` (an array of acceptable corrected
names) and/or `category` for each item. Set `reviewed` to true only after checking.
Labels are never sent to the model. Score the saved answers without any API call:

```sh
uv run python -m experiments.gemma_batch --evaluate "data/output/<run>" --review "data/output/<run>/review.json"
```

`reviewed-report.md` and `.json` report name accuracy before/after, improvement
and regression counts, and category accuracy, with explicit denominators.
Unreviewed/missing labels and cases without completed model results are excluded;
an empty denominator is unknown, never 0% or 100%. Name matching only normalizes
case and whitespace, with multiple accepted spellings supported. Metrics cover
parser-produced items; omitted products remain visible in the separate parser
evaluation and cannot be recovered by enrichment. This small sample diagnoses
behavior; it does not estimate accuracy on all receipts.

## Extended Gemma benchmark

The versioned fixtures under `experiments/fixtures/gemma-benchmark-v2/` freeze
24 documents, 115 parser items, a 12/12 development/holdout split, and assistant
image annotations prepared before this benchmark's model predictions. Annotators
inherited conversation context and three development documents were discussed
previously; this is not a fully blinded human gold-label study. No
same-transaction date/total/amount-due collision crossed this split;
this check does not prove the absence of all semantic overlap. Actual receipts
cover only a subset of the 18 categories. Missing parser names, overlapping-photo
duplicates and malformed fragments remain visible rather than silently repaired.

Prepare a plan with no model calls:

```sh
uv run python -m experiments.gemma_benchmark
```

Execute the fixed plan (two frozen prompts, both splits, plus 23 clearly labelled
synthetic challenges) with a shared reservation budget:

```sh
uv run python -m experiments.gemma_benchmark --execute --budget 0.50 --max-calls 120
```

The complete conservative plan reservation is checked before calls. Every new
attempt, including failures, is charged against the shared reservation ledger.
API-reported monetary costs are recorded separately; cache hits do not spend a
new attempt. Up to eight items per request avoid truncation on long receipts;
their evidence and original item IDs are remapped explicitly. The prompt is
selected using development results before loading holdout labels for scoring.
Both variants then receive one frozen holdout evaluation, with no automatic
retries. Labels never enter requests. Detailed requests, responses, per-case
results, prompt hashes, label hashes and the ledger are retained in the output.

The extended benchmark explicitly uses strict decoding to expose model contract
failures. The paid adapter's default mode now quarantines malformed *individual*
item fields: raw name, null category and `needs_review=true` with reason
`invalid_model_output`. Missing/reordered IDs or changed item counts still reject
the entire response. This fallback never turns an invalid item into a correct
classification. Set `resilient=False` on `PaidOpenRouterEnricher` to reproduce
strict decoding. `uncertain_name` restores the raw name. Numeric guards now
protect units and embedded size/code suffixes such as `400g`, `PB95`, `X2`, while
allowing letter OCR repair such as `D0RITO` and unit repair `500m1` → `500ml`.

The 2026-10-25 run retained the existing prompt: a stronger correction prompt
improved development names but worsened categories and did not generalize to
better holdout names. An experimental two-pass combination was also rejected.
See [benchmark findings](../../notes/gemma-benchmark-2026-10-25.md) for exact
denominators, post-hoc follow-up, limits and next steps. No model weights were
trained or fine-tuned.

## Image-only Gemma comparison

The separate experimental adapter `extraction.steps.parsing.vision` sends original
JPEG/PNG bytes directly to paid Gemma 4 31B through OpenRouter. It receives no OCR,
parser output, filenames or reference labels. One request can contain overlapping
photos of the same receipt; no local image resizing, cropping or rotation occurs.
The response contains the full receipt and the same 18 item categories. Financial
fields use strict decimal strings and the existing receipt validator. Image
references are model claims, stored separately from OCR evidence.

Prepare the fixed five-document paired comparison without API calls:

```sh
uv run python -m experiments.gemma_vision --baseline-run data/output/2026-10-25_103739_852ypuo7
```

Run it with the existing `OPENROUTER_API_KEY`:

```sh
uv run python -m experiments.gemma_vision --baseline-run data/output/2026-10-25_103739_852ypuo7 --execute
```

`--limit 1` through `5` selects a prefix of the fixed sample. `--baseline-run`
explicitly selects saved predictions; the example uses the historical study.
The directory must contain `development-baseline/report.json` and
`holdout-baseline/report.json`. `--manifest`, `--references`, `--cache-dir` and
`--output-dir` can also be supplied. Local baseline artifacts and source images
must exist. Five documents contain six images
and 28 visually verified purchases. References were checked by an assistant
before vision predictions and are not human gold labels.

There are at most five new calls, no automatic retry, and execution stops on the
first failed request or malformed response. A ledger reserves the full model
context at routing price ceilings before each attempt, with a $0.45 total cap.
Actual API costs are recorded separately. DeepInfra is preferred for image input;
image-capable fallbacks under the same price ceilings are allowed and the actual
provider is recorded. Responses, including malformed ones, are cached separately
under `.cache/gemma-vision/`. Request reports retain image hashes and omit base64.
Raw original images are sent to the provider during execution.

Replay cached responses with network calls explicitly forbidden:

```sh
uv run python -m experiments.gemma_vision --baseline-run data/output/2026-10-25_103739_852ypuo7 --replay-cache
```

The initial 2026-10-25 run made five calls for $0.003994155. Four responses passed
the initial decoder; the two-photo response used a valid aggregate image claim
`field="items"` that the decoder initially rejected. After accepting group claims,
all five original cached responses were evaluated offline with zero extra calls.
The original failure report remains intact. See
[vision findings](../../notes/gemma-vision-2026-10-25.md) for the paired results.

The output has `comparison.json`, `comparison.md`, per-document responses and
field-level evaluations. Reported API costs exclude local OCR computation;
historical pipeline timings cover text enrichment only. The comparison now uses
the standard `AnalyzeReceiptImagesStep` and validator workflow; API modes remain opt-in.

## Composable analysis workflows

`extraction.workflows.text_analysis_pipeline(client)` maps saved `OCRResult` through
context-preserving parsing, enrichment and validation.
`extraction.workflows.vision_analysis_pipeline(client)` maps original `SourceFiles`
through image extraction and the same arithmetic validator. Both return
`AnalyzedReceipt(receipt, annotations, image_evidence)`: original extracted financial
fields, aligned name/category proposals, and image references separate from OCR.
Text enrichment splits long receipts into up to eight items per request.

`main.build_pipeline(enricher=client)` includes prepare/orient/OCR before text
analysis; `main.build_pipeline(vision_client=client)` selects original-image
analysis. Calling `build_pipeline()` retains the existing local OCR/rules workflow.
Clients are supplied explicitly; these factories do not load credentials.

Step names describe their tasks: full text processing runs
`prepare → orient → ocr → parse → classify → validate`; both image workflows run
`analyze → validate`. `ClassifyReceiptItemsStep` proposes categories and corrected
names; `AnalyzeReceiptImagesStep` and `AnalyzeReceiptImagesWithOCRStep` extract
receipt fields and classify products together. New reports and `capture_steps`
use these names; historical reports are unchanged and old step classes have no aliases.

Edit default instructions in `extraction/prompts/*.md`: `classify_items`,
`analyze_receipt_images`, `analyze_receipt_images_with_ocr`, `ocr_guidance`, and
`parse_receipt_ocr`. Categories and response schemas remain defined in Python.
Resources are included in the installed package; restart the process after edits.
The benchmark-only name policy lives in `experiments/prompts/name_policy.md`.

All model clients accept a keyword `prompt=` override; vision additionally accepts
`ocr_prompt=` and `ocr_guidance=`. Overrides replace the complete rendered text,
including any appended categories/schema, while response validation stays active.
For example, `PaidOpenRouterEnricher(api_key, prompt=custom_text)` can be passed
to `text_analysis_pipeline` as usual. `None` selects defaults; blank text is invalid.
Configure clients before building a pipeline. Step configs record `prompt_id` and
`prompt_sha256`; hybrid analysis also records the OCR guidance ID and hash.
Defaults use the resource name, overrides use `custom`. Whitespace is preserved
and unchanged defaults retain their existing request bodies and cache keys.

Clients accept a public `request_policy(body, send)` callback for cache misses.
`BudgetedRequests` reserves before transport, `cache_only` forbids transport, and
the shared experiment ledger lives in `experiments.budget`. Experiment scripts no
longer replace private transport methods. Core receipt decoding lives in
`extraction.steps.parsing.common`; Gemini adds OCR provenance and vision adds image
claims without constructing fake OCR.

See the [Polish architecture and usage guide](../../notes/extraction-analysis-pipeline.md)
for composition, the output contract, budgeting and regression verification.

## Paired multimodal study

The frozen `gemma-vision-v2` references cover all 24 documents in the earlier
benchmark, with 105 unique purchases (the parser produced 115 rows, including
overlap duplicates). Assistant reviewers checked the source images, financial
fields and item categories before new model predictions. Four category boundaries
have explicitly recorded acceptable alternatives; primary and accepted-category
scores remain separate. This is a curated assistant-labelled study, not human gold.

```sh
uv run python -m experiments.gemma_multimodal --baseline-run data/output/2026-10-25_103739_852ypuo7
uv run python -m experiments.gemma_multimodal --baseline-run data/output/2026-10-25_103739_852ypuo7 --execute --budget 0.45
```

The dry run freezes request hashes, reference hashes and evaluation policies.
Execution pairs original-image Gemma with original-image-plus-OCR Gemma, using the
same model, output schema and arithmetic validator. The OCR data includes text,
confidence and normalized geometry; it never includes parser predictions or
labels. Both variants are proper pipeline steps with the shared `AnalyzedReceipt`
output. Historic text predictions provide the third comparison without new calls.
The five previously tested vision documents are reported separately from the 19
new-to-vision documents. The original 12/12 split is retained in each record.

`--limit 1` through `24` limits the frozen ordered sample. There are no automatic
retries. Execution stops on quota/configuration errors, the shared budget/call cap,
or three consecutive failed results. Failed results remain in scoring denominators.
Missing zero discounts and names with tax-code suffixes are reported explicitly.

`SettledBudget` in `experiments.budget` reserves the whole context bound before
each sequential request and records every attempt. A known provider-reported cost
settles that attempt, releasing unused headroom; timeouts and missing costs retain
the full reservation. Cumulative conservative bounds, effective commitment and
actual cost are distinct ledger fields. This permits a larger study under one
$0.45 cap rather than treating a pessimistic per-call bound as actual expenditure.
The existing cumulative `Budget` used by older experiments retains its behavior.

`--replay-cache` explicitly forbids transport and does not load credentials.
Artifacts include request summaries with image hashes, responses, typed pipeline
outputs, field evaluations, validation warnings, progress, a budget ledger and final
`comparison.json`/`.md`. Reference snapshots and prompts are not adjusted mid-run.

The completed 2026-10-25 study made 43 new calls for $0.048318795. Image-plus-OCR
recovered all 105 purchases without excess rows, with 24/24 correct dates and
105/105 correct quantities and unit prices, but three post-discount item totals
were wrong. A separate `posthoc-audit.json` exposes reference-null fills and wrong
known numbers that passed arithmetic validation. It does not replace the frozen
primary metrics. See [full findings](../../notes/gemma-multimodal-2026-10-25.md),
including annotation limits, category errors and actual latency.

## Natural hard-receipt study

Results and limitations: [Polish hard-study report](../../notes/gemma-hard-2026-10-25.md).

`experiments/fixtures/gemma-hard-v1/manifest.json` freezes seven further physical
transactions and 140 unique purchases. Five receipts have 20–31 rows with many
discounts, small/faint print, creases and fractional quantities. Two short receipts
target repeated weighted products and unrelated receipts showing in the background.
An alternative capture of one 30-item receipt and a naturally clipped first-photo
control are scored separately from the primary independent transactions.

```sh
uv run python -m experiments.gemma_hard --ocr-dir data/output/2026-10-20_232227_v_m6nrfb/without-orientation
uv run python -m experiments.gemma_hard --ocr-dir data/output/2026-10-20_232227_v_m6nrfb/without-orientation --execute --budget 0.35
```

This study compares freshly run text enrichment and image-plus-OCR using the same
frozen prompts and common pipeline steps. Model calls share a settled budget and
are never retried automatically. Raw parser snapshots, reference/request hashes,
provider responses and per-method results are retained. `--replay-cache` forbids
transport and credential loading. Primary metrics use only seven independent full
transactions; related capture and incomplete-input controls have separate cohorts.

For the incomplete input, only that original image and its corresponding OCR page
are passed to either workflow. No later image, full-document OCR, filename or
reference is sent to the model. Unseen summary/date/last-item fields are explicitly
annotated as unknown. Empty deposit groups cannot express the difference between
unknown and none in the current output contract; the control reports this limitation
rather than awarding a correct abstention. Missing numeric values and invalid review
metadata remain distinguishable from genuine incorrect populated amounts in audits.

## Team guide

See the [Polish team guide](../../notes/extraction-worker.md) for Windows/Linux
setup, optional engines, configuration, experiments, annotations, and tests.
