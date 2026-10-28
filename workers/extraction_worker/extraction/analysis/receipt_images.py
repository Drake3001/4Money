"""Receipt extraction and classification from images, optionally supported by OCR."""

import base64
import copy
import hashlib
import json
import re
from decimal import Decimal
from io import BytesIO
from pathlib import Path

from PIL import Image

from ..categories import CATEGORIES
from ..prompts import image_analysis_prompt, load_text, resolve_prompt
from ..reporting import write_json
from ..requests import dispatch_request
from ..providers import ProviderHTTPError, ProviderResponseError
from .models import PAID_MODEL
from .item_classifier import decode_enrichment
from ..steps.parsing.common import RECEIPT_SCHEMA, decode_receipt_fields
from ..steps.parsing.context import ocr_pages

OUTPUT_TOKENS = 8192
GEMINI_MODEL = "google/gemini-3.1-flash-lite"
CONTEXT_TOKENS = 262144
VISION_SCHEMA = copy.deepcopy(RECEIPT_SCHEMA)
VISION_SCHEMA["properties"]["image_evidence"] = {"type": "array", "items": {
    "type": "object", "properties": {"field": {"type": "string"},
        "images": {"type": "array", "items": {"type": "integer"}, "minItems": 1}},
    "required": ["field", "images"], "additionalProperties": False}}
VISION_SCHEMA["required"].append("image_evidence")
item_schema = VISION_SCHEMA["properties"]["items"]["items"]
item_schema["properties"].update({
    "category": {"type": ["string", "null"], "enum": [*CATEGORIES, None]},
    "needs_review": {"type": "boolean"},
    "review_reason": {"type": ["string", "null"], "enum": [None, "unreadable_name", "ambiguous_category", "uncertain_name"]},
})
item_schema["required"].extend(["category", "needs_review", "review_reason"])

VISION_PROMPT = image_analysis_prompt(VISION_SCHEMA)


def image_part(source):
    if source.mime_type not in ("image/jpeg", "image/png"):
        raise ValueError("Vision experiment accepts original JPEG/PNG images only")
    with Image.open(BytesIO(source.content)) as image:
        if image.format not in ("JPEG", "MPO", "PNG"):
            raise ValueError("Invalid source image format")
        image.verify()
    return {"type": "image_url", "image_url": {
        "url": f"data:{source.mime_type};base64," + base64.b64encode(source.content).decode("ascii")}}


OCR_GUIDANCE = load_text("ocr_guidance")
VISION_OCR_PROMPT = image_analysis_prompt(VISION_SCHEMA, with_ocr=True)


def vision_routing(model, routing):
    if model not in (PAID_MODEL, GEMINI_MODEL):
        raise ValueError("Only the configured image analysis models are permitted")
    routing = routing or ("throughput" if model == GEMINI_MODEL else "deepinfra")
    if routing not in ("deepinfra", "throughput", "latency"):
        raise ValueError("Vision routing must be deepinfra, throughput or latency")
    if model == GEMINI_MODEL and routing == "deepinfra":
        raise ValueError("Gemini vision routing must be throughput or latency; DeepInfra does not serve this model")
    return routing


def build_vision_request(sources, ocr=None, *, prompt=None, ocr_prompt=None, ocr_guidance=None,
                         model=PAID_MODEL, routing=None):
    routing = vision_routing(model, routing)
    prompt = resolve_prompt(prompt, VISION_PROMPT)
    ocr_prompt = resolve_prompt(ocr_prompt, VISION_OCR_PROMPT)
    ocr_guidance = resolve_prompt(ocr_guidance, OCR_GUIDANCE)
    if not 1 <= len(sources) <= 4:
        raise ValueError("Choose 1–4 images of one receipt")
    content = [{"type": "text", "text": "Odczytaj ten jeden dokument zakupu. Obrazy są w kolejności fragmentów."}]
    for i, source in enumerate(sources, 1):
        content.extend([{"type": "text", "text": f"Obraz {i}:"}, image_part(source)])
    if ocr is not None:
        if len(ocr.pages) != len(sources) or [p.page_number for p in ocr.pages] != list(range(1, len(sources) + 1)):
            raise ValueError("OCR pages must align with the supplied image order")
        content.append({"type": "text", "text": ocr_guidance + json.dumps(ocr_pages(ocr), ensure_ascii=False)})
    provider = {"allow_fallbacks": True,
                "max_price": {"prompt": 0.30, "completion": 1.60 if model == GEMINI_MODEL else 0.60,
                              "image": 0.01 if model == GEMINI_MODEL else 0, "request": 0}}
    provider.update({"order": ["deepinfra"]} if routing == "deepinfra" else {"sort": routing})
    payload = {"model": model, "temperature": 0, "max_tokens": OUTPUT_TOKENS, "stream": False,
            "messages": [{"role": "system", "content": prompt if ocr is None else ocr_prompt}, {"role": "user", "content": content}],
            "provider": provider}
    if model == GEMINI_MODEL:
        payload.update(reasoning={"effort": "minimal"}, service_tier="default")
    return payload


def request_summary(payload):
    result = copy.deepcopy(payload)
    for part in result["messages"][1]["content"]:
        if part["type"] == "image_url":
            url = part["image_url"]["url"]
            data = base64.b64decode(url.split(",", 1)[1])
            part["image_url"]["url"] = "<original image bytes omitted from report>"
            part["image_metadata"] = {"sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}
    return result


def vision_bound():
    # Reserve the whole model context at the routing ceiling; never treat base64 as text tokens.
    # Flat image/request charges are explicitly excluded by provider.max_price.
    return (Decimal(CONTEXT_TOKENS) * Decimal("0.30") + Decimal(OUTPUT_TOKENS) * Decimal("0.60")) / 1_000_000


def decode_vision(raw, image_count):
    expected = set(VISION_SCHEMA["properties"])
    if not isinstance(raw, dict) or set(raw) != expected or not isinstance(raw["items"], list):
        raise ValueError("Vision result does not match receipt schema")
    copied = copy.deepcopy(raw)
    evidence = copied.pop("image_evidence")
    metadata = []
    for index, item in enumerate(copied["items"]):
        if not isinstance(item, dict) or set(item) != set(item_schema["properties"]):
            raise ValueError("Vision item does not match schema")
        metadata.append({"id": index, "name_corrected": item["name"],
                         **{key: item.pop(key) for key in ("category", "needs_review", "review_reason")}})
    decoded = decode_receipt_fields(copied)
    enrichment = decode_enrichment({"items": metadata}, decoded.output)
    if not isinstance(evidence, list):
        raise ValueError("Image evidence must be a list")
    seen = set()
    for claim in evidence:
        if (not isinstance(claim, dict) or set(claim) != {"field", "images"}
                or not isinstance(claim["field"], str) or claim["field"] in seen
                or not isinstance(claim["images"], list) or not claim["images"]
                or any(type(i) is not int or not 1 <= i <= image_count for i in claim["images"])):
            raise ValueError("Invalid image evidence")
        path = claim["field"]
        match = re.fullmatch(r"(items|deposits|deposit_returns)\[(\d+)\](?:\.(\w+))?", path)
        if match:
            group = getattr(decoded.output, match[1])
            if int(match[2]) >= len(group) or match[3] and not hasattr(group[int(match[2])], match[3]):
                raise ValueError("Unknown image evidence field")
        elif path not in ("store", "date", "currency", "total", "amount_due", "items", "deposits", "deposit_returns"):
            raise ValueError("Unknown image evidence field")
        seen.add(path)
    return decoded, enrichment, evidence


class ReceiptImageAnalyzer:
    """Extract and classify a receipt from images, with optional supporting OCR."""

    def __init__(self, client, cache_dir=None, *, allow_paid=False, model=PAID_MODEL, request_policy=None,
                 prompt=None, ocr_prompt=None, ocr_guidance=None, routing=None):
        if client.provider != "openrouter" or allow_paid is not True:
            raise ValueError("Image analysis requires OpenRouter and explicit allow_paid=True")
        self.routing = vision_routing(model, routing)
        self.client = client
        self.model = model
        self.max_output_tokens = OUTPUT_TOKENS
        self.allow_paid = True
        self.prompt = resolve_prompt(prompt, VISION_PROMPT)
        self.ocr_prompt = resolve_prompt(ocr_prompt, VISION_OCR_PROMPT)
        self.ocr_guidance = resolve_prompt(ocr_guidance, OCR_GUIDANCE)
        self.prompt_id = "analyze_receipt_images" if prompt is None else "custom"
        self.ocr_prompt_id = "analyze_receipt_images_with_ocr" if ocr_prompt is None else "custom"
        self.ocr_guidance_id = "ocr_guidance" if ocr_guidance is None else "custom"
        self.cache_dir = Path(cache_dir) if cache_dir else None
        self.request_policy = request_policy
        self.response, self.cache_hit, self.enrichment, self.image_evidence = None, False, None, None

    def _generate(self, body):
        try:
            return self.client.send(body, timeout=180)
        except ProviderHTTPError as exc:
            raise ProviderHTTPError("OpenRouter vision", exc.status, "check image-capable endpoint and quota") from None

    def run(self, sources, ocr=None):
        self.response, self.cache_hit, self.enrichment, self.image_evidence = None, False, None, None
        payload = build_vision_request(sources, ocr, prompt=self.prompt,
                                       ocr_prompt=self.ocr_prompt, ocr_guidance=self.ocr_guidance,
                                       model=self.model, routing=self.routing)
        body = json.dumps(payload, sort_keys=True, ensure_ascii=False).encode("utf-8")
        digest = hashlib.sha256(body).hexdigest()
        path = self.cache_dir / f"{digest}.json" if self.cache_dir else None
        if path is not None and path.is_file():
            self.response = json.loads(path.read_text(encoding="utf-8"))
            self.cache_hit = True
        else:
            self.response = dispatch_request(body, self._generate, self.request_policy)
            if path is not None:
                path.parent.mkdir(exist_ok=True, parents=True)
                if not path.exists():
                    write_json(path, self.response)
        try:
            choices = self.response.get("choices", [])
            if len(choices) != 1 or choices[0].get("finish_reason") != "stop":
                raise ProviderResponseError()
            text = choices[0].get("message", {}).get("content")
            if not isinstance(text, str):
                raise ProviderResponseError()
            match = re.fullmatch(r"\s*```(?:json)?\s*\n(.*?)\n```\s*", text, re.DOTALL)
            raw = json.loads(match[1] if match else text)
            result, self.enrichment, self.image_evidence = decode_vision(raw, len(sources))
        except (ValueError, TypeError, KeyError, AttributeError, IndexError):
            raise ProviderResponseError() from None
        return result
