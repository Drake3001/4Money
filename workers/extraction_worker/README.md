# Receipt extraction worker

HTTP API that reads Polish receipts and returns purchases, amounts and product categories.
The default pipeline prepares and rotates images, runs RapidOCR, then sends the images
and OCR to **Gemini 3.1 Flash-Lite through OpenRouter** in one request per receipt.
Images use full-resolution JPEG 95 when smaller than PNG; OCR uses the prepared PNG.

## Run with Docker

From `workers/extraction_worker`, copy the configuration and fill in `OPENROUTER_API_KEY`:

```sh
cp .env.example .env
docker compose up --build -d
docker compose logs -f extraction-worker
```

The API is available at `http://127.0.0.1:8000`; Swagger UI is at `/docs`.
Startup downloads missing local models and warms up OCR before accepting requests.
Docker preserves downloaded models in the `model-cache` volume.

## Run locally

Install Python 3.12 and [uv](https://docs.astral.sh/uv/), then run from the worker directory:

```sh
uv sync
uv run --env-file .env uvicorn api:app --host 127.0.0.1 --port 8000 --workers 1
```

Use one worker process. Requests are processed sequentially.
Keep the service on a trusted private network; authentication belongs to the calling backend.
A backend on the same Docker network can use `http://extraction-worker:8000`.

## Configuration

Copy [.env.example](.env.example) to `.env`. Docker Compose and the local command above load it.

| Variable | Default / purpose |
|---|---|
| `OPENROUTER_API_KEY` | Required OpenRouter key |
| `EXTRACTION_WORKFLOW` | `vision_ocr`: images + OCR → Gemini; `vision`: images → Gemini; `text`: OCR → rules → Gemma classification |
| `EXTRACTION_VISION_ROUTING` | `throughput`; use `latency` to prioritize time to first token |
| `EXTRACTION_BIND_ADDRESS` | `127.0.0.1`; Docker Compose host binding |
| `EXTRACTION_MAX_FILE_BYTES` | `20971520` (20 MiB per file) |
| `EXTRACTION_MAX_REQUEST_BYTES` | `52428800` (50 MiB including multipart overhead) |
| `EXTRACTION_MAX_PAGE_PIXELS` | `50000000` (50 MP per page) |
| `EXTRACTION_MAX_DOCUMENT_PIXELS` | `100000000` (100 MP across the receipt) |

The API image model is `google/gemini-3.1-flash-lite`, configured in `api.py`.
`OPENROUTER_MODEL` and `OPENROUTER_BASE_URL` in `.env` configure local experiments.
Restart the service after configuration changes.

## Extract a receipt

`POST /api/v1/extract` accepts **1–4 files of one receipt**, in fragment order:
JPEG, PNG, WebP, HEIC/HEIF, TIFF or PDF, and BMP images. PDF/TIFF pages are expanded
in order, up to **four pages total**. HEIC/HEIF uses the primary photo; animated
images are rejected. The decoder identifies the format from file contents.
Send each file under the multipart field `files`, including for a single file:

```sh
curl --fail-with-body http://127.0.0.1:8000/api/v1/extract \
  -F 'files=@receipt-01.jpg;type=image/jpeg' \
  -F 'files=@receipt-02.png;type=image/png'
```

On Windows PowerShell, use `curl.exe` and put the command on one line.
Successful requests return HTTP 200:

```json
{
  "receipt": {
    "store": "Sklep",
    "date": "2026-10-07",
    "currency": "PLN",
    "total": "3.50",
    "amount_due": "3.50",
    "items": [{
      "name": "MLEKO",
      "quantity": "1",
      "unit": "szt",
      "unit_price": "3.50",
      "total": "3.50",
      "before_discount": "3.50",
      "discount": "0.00",
      "name_corrected": "Mleko",
      "category": "żywność i napoje",
      "needs_review": false,
      "review_reason": null
    }],
    "deposits": [],
    "deposit_returns": []
  },
  "warnings": [],
  "needs_review": false
}
```

Amounts and quantities are decimal strings, dates use ISO format, and unknown fields are `null`.
`name` is the extracted name; `name_corrected` is the proposed normalized name.
`deposits` contains issued deposits; `deposit_returns` contains returned deposits.
Standalone discounts remain in `items` with category `zniżka` and a negative `total`.
Product discounts use `discount`; deposit returns remain in `deposit_returns`.
`needs_review` is true when validation reports a warning or a product requires review.
Categories are defined in [extraction/categories.py](extraction/categories.py).

| Status | Meaning |
|---|---|
| `200` | Extracted receipt, including any review flags and warnings |
| `413` | Request, file or decoded image exceeds its limit |
| `415` | Unsupported file format or animation |
| `422` | Invalid files, file count or image content |
| `500` | Internal pipeline failure |
| `502` / `503` / `504` | Provider failure / unavailable provider / provider timeout |

Errors use `{"error":{"code":"file_too_large","message":"...","request_id":"...","details":null}}`.
`X-Request-ID` identifies the request in responses and server logs. `details` may
include the file index or limit. Debug responses use the same error structure.

## Diagnostics

- `GET /health` returns `{"status":"ok"}` while the API is running.
- `GET /docs` opens the interactive API documentation.
- `POST /api/v1/debug/extract?workflow=vision_ocr` accepts the same files and returns
  step timings, configuration, OCR and extraction results. Available workflows:
  `vision_ocr`, `vision`, `text`. Debug responses contain receipt text and serve development use.

## Tests and experiments

```sh
uv run --extra test pytest -q --basetemp .cache/test-temp/pytest
```

Tests use substitutes for OCR and provider calls.
See [experiments/README.md](experiments/README.md) for local batch processing,
benchmarks and model comparisons.
