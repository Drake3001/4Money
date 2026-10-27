"""Receipt item classification, response validation and task-owned cache."""

import hashlib
import json
import re
from decimal import Decimal
from pathlib import Path

from ..categories import CATEGORIES, REVIEW_REASONS
from ..contracts import ReceiptData
from ..prompts import classification_prompt, resolve_prompt
from ..providers import ProviderHTTPError
from ..reporting import write_json
from ..requests import dispatch_request
from .models import FREE_MODEL, PAID_MODEL, GOOGLE_MODEL, NVIDIA_MODEL

PROMPT = classification_prompt()


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


class ReceiptItemClassifier:
    """Correct product names and classify items through a configured API client.

    Only the existing experiment models are enabled. Paid OpenRouter requests
    require allow_paid=True; no model, routing or price defaults are broadened.
    """

    def __init__(self, client, *, model=None, allow_paid=False, cache_dir=None,
                 prompt=None, max_output_tokens=None, resilient=None, request_policy=None):
        if type(allow_paid) is not bool:
            raise ValueError("allow_paid must be a boolean")
        self.client = client
        self.provider = client.provider
        if self.provider not in ("openrouter", "google", "nvidia"):
            raise ValueError("Unsupported classification provider")
        if allow_paid and self.provider != "openrouter":
            raise ValueError("Paid classification is supported only through OpenRouter")
        if self.provider == "openrouter":
            expected = PAID_MODEL if allow_paid else FREE_MODEL
        elif self.provider == "google":
            expected = GOOGLE_MODEL
        else:
            expected = NVIDIA_MODEL
        if model is not None and model != expected:
            raise ValueError("Only the explicitly selected classification model is permitted")
        if allow_paid:
            max_output_tokens = 1024 if max_output_tokens is None else max_output_tokens
            if type(max_output_tokens) is not int or not 256 <= max_output_tokens <= 8192:
                raise ValueError("Output token cap must be between 256 and 8192")
        elif max_output_tokens is not None:
            raise ValueError("Custom output token caps require explicit paid mode")
        self.model = expected
        self.allow_paid = allow_paid
        self.max_output_tokens = max_output_tokens if allow_paid else (1024 if self.provider == "nvidia" else 4096)
        self.resilient = allow_paid if resilient is None else bool(resilient)
        self.prompt = resolve_prompt(prompt, PROMPT)
        self.prompt_id = "classify_items" if prompt is None else "custom"
        self.cache_dir = Path(cache_dir) if cache_dir is not None else None
        self.response, self.cache_hit = None, False
        self.request_policy = request_policy

    def build_request(self, receipt, ocr):
        payload = build_request(receipt, ocr, prompt=self.prompt)
        if self.provider == "google":
            return {
                "systemInstruction": {"parts": [{"text": self.prompt}]},
                "contents": [{"role": "user", "parts": [{"text": payload["messages"][1]["content"]}]}],
                "generationConfig": {"temperature": 0, "maxOutputTokens": 4096,
                                     "thinkingConfig": {"thinkingLevel": "minimal"}},
            }
        payload["model"] = self.model
        if self.provider == "nvidia":
            del payload["provider"]
            payload["chat_template_kwargs"] = {"enable_thinking": False}
            payload["max_tokens"] = 1024
        elif self.allow_paid:
            payload["max_tokens"] = self.max_output_tokens
            payload["provider"] = {
                "order": ["coreweave", "deepinfra"], "allow_fallbacks": True,
                "max_price": {"prompt": 0.30, "completion": 0.60},
            }
        return payload

    def _generate(self, body):
        if self.provider == "google":
            try:
                raw = self.client.send(body, model=self.model)
            except ProviderHTTPError as exc:
                raise ProviderHTTPError("Google Gemma", exc.status, "check key, model access and quota") from None
            except RuntimeError:
                raise RuntimeError("Google Gemma network request failed; no automatic retry was made") from None
            # The task consumes the same envelope for every provider. Transport
            # itself returns Google's original response, also usable by Gemini.
            return {"provider": "Google AI Studio (direct)", "model": raw.get("modelVersion"),
                    "usage": raw.get("usageMetadata"), "choices": [
                        {"finish_reason": "stop" if candidate.get("finishReason") == "STOP" else "incomplete",
                         "message": {"content": "".join(
                             part.get("text", "") for part in candidate.get("content", {}).get("parts", [])
                             if not part.get("thought"))}}
                        for candidate in raw.get("candidates", [])]}
        raw = self.client.send(body)
        raw.setdefault("provider", self.client.provider_name)
        return raw

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
        decoder = decode_with_review_fallback if self.resilient else decode_enrichment
        result = decoder(json.loads(match[1] if match else content), receipt)
        if path is not None and not self.cache_hit:
            path.parent.mkdir(parents=True, exist_ok=True)
            if not path.exists():
                write_json(path, self.response)
        return result

    @staticmethod
    def request_bound(payload):
        # UTF-8 byte count plus overhead deliberately overestimates text tokens.
        # Prices are USD per million tokens, constrained by provider.max_price.
        size = len(json.dumps(payload, ensure_ascii=False).encode("utf-8")) + 4096
        return (Decimal(size) * Decimal("0.30")
                + Decimal(payload["max_tokens"]) * Decimal("0.60")) / 1_000_000
