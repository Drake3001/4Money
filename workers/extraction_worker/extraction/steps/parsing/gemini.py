"""Gemini receipt parsing: OCR geometry in, shared receipt contract out."""

import hashlib
import json
import re
from decimal import Decimal
from pathlib import Path

from ...providers import ProviderHTTPError
from ...prompts import load_text, prompt_metadata, resolve_prompt
from ...contracts import (
    ExtractionWarning, FieldEvidence, OCRResult, ReceiptData,
    StepResult,
)
from ...reporting import write_json
from .context import ocr_pages
from .common import (
    DEPOSIT_SCHEMA, ITEM_SCHEMA, NUMERIC, TEXT, object_schema, decode_receipt_fields, populated_fields,
)

MAX_OUTPUT_TOKENS = 16384
PROMPT = load_text("parse_receipt_ocr")


SCHEMA = object_schema({
    "store": TEXT, "date": {**TEXT, "description": "YYYY-MM-DD or null"}, "currency": TEXT,
    "total": NUMERIC, "amount_due": NUMERIC,
    "items": {"type": "array", "items": ITEM_SCHEMA},
    "deposits": {"type": "array", "items": DEPOSIT_SCHEMA},
    "deposit_returns": {"type": "array", "items": DEPOSIT_SCHEMA},
    "evidence": {"type": "array", "items": object_schema({
        "field": {"type": "string"},
        "fragments": {"type": "array", "items": {"type": "string"}, "minItems": 1},
        "method": {"type": "string", "enum": ["observed", "derived"]},
    })},
    "issues": {"type": "array", "items": {"type": "string"}},
})


def build_request(ocr, *, prompt=None):
    pages = ocr_pages(ocr)
    return {"systemInstruction": {"parts": [{"text": resolve_prompt(prompt, PROMPT)}]},
            "contents": [{"role": "user", "parts": [{"text": json.dumps(pages, ensure_ascii=False)}]}],
            "generationConfig": {"temperature": 0, "candidateCount": 1,
                                 "maxOutputTokens": MAX_OUTPUT_TOKENS,
                                 "responseMimeType": "application/json", "responseJsonSchema": SCHEMA}}



def decode_receipt(raw, ocr):
    if not isinstance(raw, dict) or set(raw) != set(SCHEMA["properties"]):
        raise ValueError("Gemini receipt does not match the expected fields")
    decoded = decode_receipt_fields({key: value for key, value in raw.items() if key != "evidence"})
    receipt = decoded.output
    populated = populated_fields(receipt)
    refs = {f"p{page.page_number}:f{index}": (page.page_number, index)
            for page in ocr.pages for index in range(len(page.lines))}
    if not isinstance(raw["evidence"], list):
        raise ValueError("Gemini evidence must be an array")
    for entry in raw["evidence"]:
        if (not isinstance(entry, dict) or set(entry) != {"field", "fragments", "method"}
                or not isinstance(entry["field"], str) or entry["field"] not in populated
                or entry["field"] in receipt.evidence
                or entry["method"] not in ("observed", "derived")
                or not isinstance(entry["fragments"], list) or not entry["fragments"]
                or any(not isinstance(ref, str) or ref not in refs for ref in entry["fragments"])):
            raise ValueError("Gemini evidence has an unknown field, fragment, or method")
        receipt.evidence[entry["field"]] = FieldEvidence(
            tuple(dict.fromkeys(refs[ref] for ref in entry["fragments"])), entry["method"])
    warnings = list(decoded.warnings)
    warnings.extend(ExtractionWarning("missing_ai_evidence", "No source evidence returned", field)
                    for field in sorted(populated - receipt.evidence.keys()))
    source_text = {f"p{page.page_number}:f{index}": fragment.text
                   for page in ocr.pages for index, fragment in enumerate(page.lines)}
    for entry in raw["evidence"]:
        path = entry["field"]
        group_match = re.fullmatch(r"(items|deposits|deposit_returns)\[(\d+)\]\.(\w+)", path)
        obj, field = (getattr(receipt, group_match[1])[int(group_match[2])], group_match[3]) if group_match else (receipt, path)
        value = getattr(obj, field)
        if not isinstance(value, Decimal) or entry["method"] != "observed":
            continue
        tokens = [Decimal(match.group().replace("−", "-").replace(",", ".").replace(" ", ""))
                  for ref in entry["fragments"] for match in re.finditer(
                      r"(?<![\d.,])[-−]?\s*\d+(?:[.,]\d+)?(?![\d.,])", source_text[ref])]
        if value not in tokens:
            warnings.append(ExtractionWarning("unsupported_ai_value", "Observed number absent from cited OCR fragments", path))
    return StepResult(receipt, tuple(warnings))


class GeminiParseStep:
    input_type = OCRResult
    output_type = ReceiptData
    version = "1"

    def __init__(self, client, model="gemini-3.1-flash-lite", cache_dir=None, *, prompt=None):
        if not re.fullmatch(r"gemini-[A-Za-z0-9._-]+", model):
            raise ValueError("Invalid Gemini model name")
        if client.provider != "google":
            raise ValueError("Gemini parsing requires a GoogleClient")
        self.client = client
        self.model = model
        self.prompt = resolve_prompt(prompt, PROMPT)
        self.prompt_id = "parse_receipt_ocr" if prompt is None else "custom"
        self.cache_dir = Path(cache_dir) if cache_dir is not None else None
        self.config = {"client": type(client).__name__, "provider": client.provider, "model": model,
                       **prompt_metadata(self.prompt, self.prompt_id),
                       "max_output_tokens": MAX_OUTPUT_TOKENS}
        self.response = None
        self.cache_hit = False
        self.api_failed = False

    def _generate(self, body):
        try:
            return self.client.send(body, model=self.model, timeout=90)
        except ProviderHTTPError as exc:
            self.api_failed = True
            raise RuntimeError(f"Gemini HTTP {exc.status}; check key, model access, quota and request schema") from None
        except RuntimeError:
            self.api_failed = True
            raise RuntimeError("Gemini network request failed; no automatic retry was made") from None

    def run(self, ocr):
        self.response, self.cache_hit, self.api_failed = None, False, False
        body = json.dumps(build_request(ocr, prompt=self.prompt), ensure_ascii=False, sort_keys=True).encode("utf-8")
        digest = hashlib.sha256(self.model.encode() + b"\0" + body).hexdigest()
        path = self.cache_dir / f"{digest}.json" if self.cache_dir is not None else None
        if path is not None and path.is_file():
            self.response = json.loads(path.read_text(encoding="utf-8"))
            self.cache_hit = True
        else:
            self.response = self._generate(body)
            if path is not None:
                self.cache_dir.mkdir(parents=True, exist_ok=True)
                write_json(path, self.response)
        candidates = self.response.get("candidates", [])
        if len(candidates) != 1 or candidates[0].get("finishReason") != "STOP":
            raise ValueError("Gemini response was blocked, truncated, or incomplete")
        text = "".join(part.get("text", "") for part in candidates[0].get("content", {}).get("parts", [])
                       if not part.get("thought"))
        return decode_receipt(json.loads(text), ocr)
