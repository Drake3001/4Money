"""Optional name correction/classification; parser amounts and items stay intact."""

import hashlib
import json
import re
from decimal import Decimal
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, Request, build_opener

from ..prompts import classification_prompt, resolve_prompt
from ..reporting import write_json
from ..requests import dispatch_request
from ..contracts import ReceiptData
from ..categories import CATEGORIES, REVIEW_REASONS, TAXONOMY_VERSION

FREE_MODEL = "google/gemma-4-31b-it:free"
PAID_MODEL = "google/gemma-4-31b-it"
GOOGLE_MODEL = "gemma-4-31b-it"
NVIDIA_MODEL = "google/gemma-4-31b-it"
NVIDIA_BASE_URL = "https://integrate.api.nvidia.com/v1"
BASE_URL = "https://openrouter.ai/api/v1"
PROMPT = classification_prompt()


class _NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class ProviderHTTPError(RuntimeError):
    """Safe, structured HTTP failure without a provider body or credentials."""

    def __init__(self, provider, status, hint):
        self.status = status
        super().__init__(f"{provider} HTTP {status}; {hint}; transport made no automatic retry")


def build_request(receipt, ocr, model=FREE_MODEL, *, prompt=None):
    if model != FREE_MODEL:
        raise ValueError("This experiment only permits Gemma 4 31B :free")
    fragments = {(page.page_number, i): line.text
                 for page in ocr.pages for i, line in enumerate(page.lines)}
    items = []
    for i, item in enumerate(receipt.items):
        evidence = receipt.evidence.get(f"items[{i}].name")
        items.append({"id": i, "name_raw": item.name,
                      "ocr_context": [fragments[ref] for ref in evidence.fragments
                                      if ref in fragments] if evidence else []})
    store_evidence = receipt.evidence.get("store")
    context = {"store_raw": receipt.store,
               "store_ocr": [fragments[ref] for ref in store_evidence.fragments
                             if ref in fragments] if store_evidence else []}
    return {
        "model": model,
        "messages": [{"role": "system", "content": resolve_prompt(prompt, PROMPT)},
                     {"role": "user", "content": json.dumps({"context": context, "items": items}, ensure_ascii=False)}],
        "temperature": 0, "max_tokens": 4096, "stream": False,
        "provider": {"max_price": {"prompt": 0, "completion": 0}},
    }


def numeric_name_facts(text):
    """Keep readable sizes/codes, allowing OCR glyph repair inside ordinary words."""
    text = (text or "").casefold()
    facts, consumed = [], 0
    unit_pattern = re.compile(r"\s*(kg|mg|ml|m1|cl|dl|cm|mm|szt|g|l|m|%)(?![a-z0-9])")
    for match in re.finditer(r"\d+(?:[.,]\d+)?", text):
        if match.start() < consumed:
            continue
        unit = unit_pattern.match(text, match.end())
        before = text[match.start()-1:match.start()] if match.start() else ""
        after = text[match.end():match.end()+1]
        if unit is None and before.isalpha() and after.isalpha():
            continue  # D0RITO is a letter-repair candidate, unlike PB95 or X2.
        suffix = unit[1].replace("m1", "ml") if unit else ""
        consumed = unit.end() if unit else match.end()
        facts.append((Decimal(match[0].replace(",", ".")), suffix))
    return sorted(facts)


def decode_enrichment(raw, receipt):
    if not isinstance(raw, dict) or set(raw) != {"items"} or not isinstance(raw["items"], list):
        raise ValueError("Gemma returned an invalid enrichment object")
    if len(raw["items"]) != len(receipt.items):
        raise ValueError("Gemma changed the number of items")
    result = []
    for i, entry in enumerate(raw["items"]):
        if (not isinstance(entry, dict) or set(entry) != {"id", "name_corrected", "category", "needs_review", "review_reason"}
                or type(entry["id"]) is not int or entry["id"] != i
                or (entry["category"] is not None and entry["category"] not in CATEGORIES)
                or type(entry["needs_review"]) is not bool
                or (entry["needs_review"] and entry["review_reason"] not in REVIEW_REASONS)
                or (not entry["needs_review"] and entry["review_reason"] is not None)
                or (not entry["needs_review"] and (entry["category"] is None or entry["name_corrected"] is None))
                or (entry["category"] is None and entry["review_reason"] not in ("unreadable_name", "ambiguous_category", "invalid_model_output"))
                or (entry["name_corrected"] is not None
                    and (not isinstance(entry["name_corrected"], str)
                         or not entry["name_corrected"].strip()))):
            raise ValueError("Gemma returned an invalid item, order or category")
        if entry["name_corrected"] is None and not (entry["category"] is None and entry["review_reason"] == "unreadable_name"):
            raise ValueError("Unreadable names must have a null category and explicit review reason")
        if entry["review_reason"] == "unreadable_name" and (entry["name_corrected"] is not None or entry["category"] is not None):
            raise ValueError("Unreadable reason requires a null name and category")
        if entry["review_reason"] == "uncertain_name":
            entry = {**entry, "name_corrected": receipt.items[i].name}
        if not receipt.items[i].name or not receipt.items[i].name.strip():
            entry = {**entry, "name_corrected": None, "category": None,
                     "needs_review": True, "review_reason": "unreadable_name"}
        if entry["name_corrected"] is not None and numeric_name_facts(receipt.items[i].name) != numeric_name_facts(entry["name_corrected"]):
            entry = {**entry, "name_corrected": receipt.items[i].name, "needs_review": True,
                     "review_reason": "ambiguous_category" if entry["category"] is None else "unsupported_name_change"}
            if entry["name_corrected"] is None:
                entry.update(category=None, review_reason="unreadable_name")
        result.append({**entry, "name_raw": receipt.items[i].name})
    return {"items": result}


def decode_with_review_fallback(raw, receipt):
    """Quarantine malformed item fields, but never guess alignment or add purchases."""
    if (not isinstance(raw, dict) or set(raw) != {"items"} or not isinstance(raw["items"], list)
            or len(raw["items"]) != len(receipt.items)):
        raise ValueError("Cannot safely align model items to parser items")
    for i, entry in enumerate(raw["items"]):
        if not isinstance(entry, dict) or type(entry.get("id")) is not int or entry["id"] != i:
            raise ValueError("Cannot safely align reordered or missing model ids")
    results = []
    for i, entry in enumerate(raw["items"]):
        try:
            single = ReceiptData(items=[receipt.items[i]])
            item = decode_enrichment({"items": [{**entry, "id": 0}]}, single)["items"][0]
            item["id"] = i
        except ValueError:
            name = receipt.items[i].name
            item = {"id": i, "name_raw": name, "name_corrected": name if name and name.strip() else None,
                    "category": None, "needs_review": True,
                    "review_reason": "invalid_model_output" if name and name.strip() else "unreadable_name"}
        results.append(item)
    return {"items": results}


class OpenRouterEnricher:
    base_url = BASE_URL
    provider_name = "OpenRouter"

    def __init__(self, api_key, *, model=FREE_MODEL, base_url=BASE_URL, cache_dir=None, request_policy=None, prompt=None):
        if model != FREE_MODEL or base_url.rstrip("/") != BASE_URL:
            raise ValueError("Only the official OpenRouter URL and free Gemma model are permitted")
        if not re.fullmatch(r"[A-Za-z0-9_-]+", api_key):
            raise ValueError("Missing or invalid OPENROUTER_API_KEY")
        self._api_key = api_key
        self.model = model
        self.prompt = resolve_prompt(prompt, PROMPT)
        self.prompt_id = "classify_items" if prompt is None else "custom"
        self.cache_dir = Path(cache_dir) if cache_dir is not None else None
        self.response = None
        self.cache_hit = False
        self.request_policy = request_policy

    def build_request(self, receipt, ocr):
        return build_request(receipt, ocr, self.model, prompt=self.prompt)

    def _generate(self, body):
        request = Request(self.base_url + "/chat/completions", data=body, method="POST",
                          headers={"Authorization": "Bearer " + self._api_key,
                                   "Content-Type": "application/json"})
        try:
            with build_opener(_NoRedirect()).open(request, timeout=60) as response:
                raw = json.load(response)
                raw.setdefault("provider", self.provider_name)
                return raw
        except HTTPError as exc:
            # Do not expose provider bodies, which may echo credentials or documents.
            raise ProviderHTTPError(self.provider_name, exc.code, "check key, model availability and quota") from None
        except TimeoutError:
            raise RuntimeError(f"{self.provider_name} response timed out after 60 seconds; no automatic retry was made") from None
        except URLError:
            raise RuntimeError(f"{self.provider_name} network request failed; no automatic retry was made") from None

    def run(self, receipt, ocr):
        self.response, self.cache_hit = None, False
        if not receipt.items:
            return {"items": []}
        body = json.dumps(self.build_request(receipt, ocr), ensure_ascii=False,
                          sort_keys=True).encode("utf-8")
        digest = hashlib.sha256(self.model.encode() + b"\0" + body).hexdigest()
        path = self.cache_dir / f"{digest}.json" if self.cache_dir else None
        if path is not None and path.is_file():
            self.response = json.loads(path.read_text(encoding="utf-8"))
            self.cache_hit = True
        else:
            self.response = dispatch_request(body, self._generate, self.request_policy)
        choices = self.response.get("choices", [])
        if len(choices) != 1 or choices[0].get("finish_reason") != "stop":
            raise ValueError("Gemma response was blocked, truncated or incomplete")
        content = choices[0].get("message", {}).get("content")
        if not isinstance(content, str):
            raise ValueError("Gemma returned no text")
        # Some providers wrap JSON in a code fence even when explicitly asked not to.
        match = re.fullmatch(r"\s*```(?:json)?\s*\n(.*?)\n```\s*", content, re.DOTALL)
        decoder = decode_with_review_fallback if getattr(self, "resilient", False) else decode_enrichment
        result = decoder(json.loads(match[1] if match else content), receipt)
        if path is not None and not self.cache_hit:
            path.parent.mkdir(parents=True, exist_ok=True)
            if not path.exists():
                write_json(path, self.response)
        return result


class PaidOpenRouterEnricher(OpenRouterEnricher):
    """Explicitly opted-in paid Gemma, with bounded token count and routing prices."""

    def __init__(self, api_key, *, base_url=BASE_URL, cache_dir=None, prompt=None, max_output_tokens=1024, resilient=True, request_policy=None):
        super().__init__(api_key, base_url=base_url, cache_dir=cache_dir, request_policy=request_policy, prompt=prompt)
        if type(max_output_tokens) is not int or not 256 <= max_output_tokens <= 8192:
            raise ValueError("Output token cap must be between 256 and 8192")
        self.model = PAID_MODEL
        self.max_output_tokens = max_output_tokens
        self.resilient = bool(resilient)

    def build_request(self, receipt, ocr):
        payload = build_request(receipt, ocr, prompt=self.prompt)
        payload["model"] = self.model
        payload["max_tokens"] = self.max_output_tokens
        payload["provider"] = {
            "order": ["coreweave", "deepinfra"], "allow_fallbacks": True,
            "max_price": {"prompt": 0.30, "completion": 0.60},
        }
        return payload

    @staticmethod
    def request_bound(payload):
        # UTF-8 byte count plus overhead deliberately overestimates text tokens.
        # Prices are USD per million tokens, constrained by provider.max_price.
        size = len(json.dumps(payload, ensure_ascii=False).encode("utf-8")) + 4096
        return (Decimal(size) * Decimal("0.30")
                + Decimal(payload["max_tokens"]) * Decimal("0.60")) / 1_000_000


class NvidiaGemmaEnricher(OpenRouterEnricher):
    """NVIDIA's hosted prototype endpoint, with a fixed Gemma model and URL."""

    base_url = NVIDIA_BASE_URL
    provider_name = "NVIDIA NIM"

    def __init__(self, api_key, *, model=NVIDIA_MODEL, cache_dir=None, request_policy=None, prompt=None):
        if model != NVIDIA_MODEL:
            raise ValueError("Only NVIDIA's Gemma 4 31B prototype model is permitted")
        if not re.fullmatch(r"[A-Za-z0-9_-]+", api_key):
            raise ValueError("Missing or invalid NVIDIA_API_KEY")
        self._api_key = api_key
        self.model = model
        self.prompt = resolve_prompt(prompt, PROMPT)
        self.prompt_id = "classify_items" if prompt is None else "custom"
        self.cache_dir = Path(cache_dir) if cache_dir is not None else None
        self.response = None
        self.cache_hit = False
        self.request_policy = request_policy

    def build_request(self, receipt, ocr):
        payload = build_request(receipt, ocr, prompt=self.prompt)
        payload["model"] = self.model
        del payload["provider"]  # OpenRouter routing options do not apply to NVIDIA.
        payload["chat_template_kwargs"] = {"enable_thinking": False}
        payload["max_tokens"] = 1024
        return payload


class GoogleGemmaEnricher(OpenRouterEnricher):
    """Same enrichment contract through Google's free Gemma generateContent API."""

    def __init__(self, api_key, *, model=GOOGLE_MODEL, cache_dir=None, request_policy=None, prompt=None):
        if model != GOOGLE_MODEL:
            raise ValueError("Only Google's free Gemma 4 31B model is permitted")
        if not re.fullmatch(r"[A-Za-z0-9_-]+", api_key):
            raise ValueError("Missing or invalid GEMINI_API_KEY")
        self._api_key = api_key
        self.model = model
        self.prompt = resolve_prompt(prompt, PROMPT)
        self.prompt_id = "classify_items" if prompt is None else "custom"
        self.cache_dir = Path(cache_dir) if cache_dir is not None else None
        self.response = None
        self.cache_hit = False
        self.request_policy = request_policy

    def build_request(self, receipt, ocr):
        shared = build_request(receipt, ocr, prompt=self.prompt)
        return {
            "systemInstruction": {"parts": [{"text": self.prompt}]},
            "contents": [{"role": "user", "parts": [{"text": shared["messages"][1]["content"]}]}],
            "generationConfig": {"temperature": 0, "maxOutputTokens": 4096,
                                 "thinkingConfig": {"thinkingLevel": "minimal"}},
        }

    def _generate(self, body):
        request = Request(
            f"https://generativelanguage.googleapis.com/v1beta/models/{self.model}:generateContent",
            data=body, method="POST",
            headers={"Content-Type": "application/json", "x-goog-api-key": self._api_key})
        try:
            with build_opener(_NoRedirect()).open(request, timeout=60) as response:
                raw = json.load(response)
        except HTTPError as exc:
            hint = ("provider server error" if exc.code >= 500 else
                    "rate limit or project quota" if exc.code == 429 else
                    "check key and model access" if exc.code in (401, 403, 404) else
                    "check request parameters")
            raise ProviderHTTPError("Google Gemma", exc.code, hint) from None
        except (URLError, TimeoutError):
            raise RuntimeError("Google Gemma network request failed; no automatic retry was made") from None
        candidates = raw.get("candidates", [])
        # Normalize only the response envelope; retain Google's usage and model version.
        return {"provider": "Google AI Studio (direct)", "model": raw.get("modelVersion"),
                "usage": raw.get("usageMetadata"), "choices": [
                    {"finish_reason": "stop" if candidate.get("finishReason") == "STOP" else "incomplete",
                     "message": {"content": "".join(
                         part.get("text", "") for part in candidate.get("content", {}).get("parts", [])
                         if not part.get("thought"))}}
                    for candidate in candidates]}
