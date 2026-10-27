import copy
import json
from decimal import Decimal
from urllib.error import HTTPError

import pytest

from experiments.gemma import load_config
from extraction.providers import GoogleClient, NvidiaClient, OpenRouterClient
from extraction.contracts import OCRResult, PreparedDocument, ReceiptData, ReceiptItem
from extraction.analysis.item_classifier import FREE_MODEL, ReceiptItemClassifier, build_request, decode_enrichment


def inputs():
    return (ReceiptData(items=[ReceiptItem(name="Mleko", total=Decimal("4.00")),
                               ReceiptItem(name="Mleko", total=Decimal("4.00"))]),
            OCRResult(PreparedDocument((), ()), (), "test", "1", {}))


def answer():
    return {"items": [{"id": i, "name_corrected": "Mleko", "category": "żywność i napoje", "needs_review": False, "review_reason": None}
                      for i in range(2)]}


@pytest.mark.parametrize("model", ["google/gemma-4-31b-it", "openrouter/free", "other:free"])
def test_only_explicit_free_gemma_is_allowed(model):
    with pytest.raises(ValueError):
        ReceiptItemClassifier(OpenRouterClient("test-key"), model=model)


def test_request_has_zero_price_ceiling_and_no_model_fallback():
    payload = build_request(*inputs())
    assert payload["model"] == FREE_MODEL
    assert payload["provider"]["max_price"] == {"prompt": 0, "completion": 0}
    assert "models" not in payload
    assert "4.00" not in json.dumps(payload)
    with pytest.raises(ValueError):
        ReceiptItemClassifier(OpenRouterClient("test-key", base_url="https://example.com"))


@pytest.mark.parametrize("damage", ["drop", "reorder", "price", "category", "bool_id"])
def test_reject_changes_to_item_identity_and_unexpected_fields(damage):
    raw = answer()
    if damage == "drop":
        raw["items"].pop()
    elif damage == "reorder":
        raw["items"].reverse()
    elif damage == "price":
        raw["items"][0]["total"] = "0.00"
    elif damage == "category":
        raw["items"][0]["category"] = "made up"
    else:
        raw["items"][0]["id"] = False
    with pytest.raises(ValueError):
        decode_enrichment(raw, inputs()[0])


def test_cached_response_preserves_repeated_items_and_parser_numbers(tmp_path, monkeypatch):
    receipt, ocr = inputs()
    before = copy.deepcopy(receipt)
    client = ReceiptItemClassifier(OpenRouterClient("test-key"), cache_dir=tmp_path)
    response = {"choices": [{"finish_reason": "stop", "message": {
        "content": "```json\n" + json.dumps(answer()) + "\n```"}}]}
    calls = []
    def generate(body):
        calls.append(body)
        return response
    monkeypatch.setattr(client, "_generate", generate)
    assert len(client.run(receipt, ocr)["items"]) == 2
    assert not client.cache_hit
    client.run(receipt, ocr)
    assert client.cache_hit and len(calls) == 1
    assert receipt == before
    assert "test-key" not in next(tmp_path.iterdir()).read_text()


def test_http_failure_does_not_expose_provider_body_or_key(monkeypatch):
    class Opener:
        def open(self, request, timeout):
            raise HTTPError(request.full_url, 429, "secret-provider-body", {}, None)
    monkeypatch.setattr("extraction.providers.http.build_opener", lambda *args: Opener())
    client = ReceiptItemClassifier(OpenRouterClient("test-key"))
    with pytest.raises(RuntimeError, match="HTTP 429") as exc:
        client.run(*inputs())
    assert "secret" not in str(exc.value) and "test-key" not in str(exc.value)


def test_config_loads_only_named_values_and_environment_overrides_file(tmp_path, monkeypatch):
    for name in ("OPENROUTER_API_KEY", "OPENROUTER_MODEL", "OPENROUTER_BASE_URL"):
        monkeypatch.delenv(name, raising=False)
    env = tmp_path / ".env"
    env.write_text('\ufeffOPENROUTER_API_KEY="test-key"\nUNRELATED=ignored\n', encoding="utf-8")
    assert load_config(env)["api_key"] == "test-key"
    monkeypatch.setenv("OPENROUTER_API_KEY", "override")
    assert load_config(env)["api_key"] == "override"


def test_truncated_answer_is_rejected_and_not_cached(tmp_path, monkeypatch):
    client = ReceiptItemClassifier(OpenRouterClient("test-key"), cache_dir=tmp_path)
    monkeypatch.setattr(client, "_generate", lambda body: {"choices": [{"finish_reason": "length"}]})
    with pytest.raises(ValueError, match="incomplete"):
        client.run(*inputs())
    assert not list(tmp_path.iterdir())


def test_google_uses_same_product_input_and_only_gemma_model():
    receipt, ocr = inputs()
    client = ReceiptItemClassifier(GoogleClient("test-key"))
    payload = client.build_request(receipt, ocr)
    assert payload["contents"][0]["parts"][0]["text"] == build_request(receipt, ocr)["messages"][1]["content"]
    assert payload["generationConfig"]["thinkingConfig"] == {"thinkingLevel": "minimal"}
    assert "test-key" not in json.dumps(payload)
    with pytest.raises(ValueError):
        ReceiptItemClassifier(GoogleClient("test-key"), model="gemini-3.1-flash-lite")


def test_google_response_envelope_ignores_thoughts_and_keeps_usage(monkeypatch):
    import io
    raw = {"modelVersion": "gemma-4-31b-it", "usageMetadata": {"totalTokenCount": 123},
           "candidates": [{"finishReason": "STOP", "content": {"parts": [
               {"text": "not JSON thinking", "thought": True},
               {"text": json.dumps(answer())}]}}]}
    class Opener:
        def open(self, request, timeout):
            assert request.full_url.endswith("/models/gemma-4-31b-it:generateContent")
            assert "test-key" not in request.full_url
            return io.BytesIO(json.dumps(raw).encode())
    monkeypatch.setattr("extraction.providers.http.build_opener", lambda *args: Opener())
    client = ReceiptItemClassifier(GoogleClient("test-key"))
    assert len(client.run(*inputs())["items"]) == 2
    assert client.response["usage"] == {"totalTokenCount": 123}


def test_google_http_errors_do_not_leak_key(monkeypatch):
    class Opener:
        def open(self, request, timeout):
            raise HTTPError(request.full_url, 403, "test-key", {}, None)
    monkeypatch.setattr("extraction.providers.http.build_opener", lambda *args: Opener())
    with pytest.raises(RuntimeError, match="Google Gemma HTTP 403") as exc:
        ReceiptItemClassifier(GoogleClient("test-key")).run(*inputs())
    assert "test-key" not in str(exc.value)


def test_nvidia_uses_fixed_endpoint_shared_validation_and_no_router_parameters(monkeypatch):
    import io
    response = {"choices": [{"finish_reason": "stop", "message": {"content": json.dumps(answer())}}],
                "usage": {"total_tokens": 100}}
    class Opener:
        def open(self, request, timeout):
            assert request.full_url == "https://integrate.api.nvidia.com/v1/chat/completions"
            assert request.get_header("Authorization") == "Bearer nvapi-test-key"
            payload = json.loads(request.data)
            assert payload["model"] == "google/gemma-4-31b-it"
            assert payload["chat_template_kwargs"] == {"enable_thinking": False}
            assert payload["max_tokens"] == 1024
            assert "provider" not in payload and "models" not in payload
            assert "nvapi-test-key" not in request.data.decode()
            return io.BytesIO(json.dumps(response).encode())
    monkeypatch.setattr("extraction.providers.http.build_opener", lambda *args: Opener())
    client = ReceiptItemClassifier(NvidiaClient("nvapi-test-key"))
    assert len(client.run(*inputs())["items"]) == 2
    assert client.response["provider"] == "NVIDIA NIM"
    with pytest.raises(ValueError):
        ReceiptItemClassifier(NvidiaClient("test-key"), model="other-model")


def test_nvidia_config_never_uses_another_providers_key(tmp_path, monkeypatch):
    monkeypatch.delenv("NVIDIA_API_KEY", raising=False)
    monkeypatch.setenv("OPENROUTER_API_KEY", "unrelated-secret")
    env = tmp_path / ".env"
    env.write_text('NVIDIA_API_KEY="nvapi-local"\nGEMINI_API_KEY=unrelated\n', encoding="utf-8")
    assert load_config(env, provider="nvidia") == {"api_key": "nvapi-local"}
    monkeypatch.setenv("NVIDIA_API_KEY", "nvapi-override")
    assert load_config(env, provider="nvidia") == {"api_key": "nvapi-override"}


@pytest.mark.parametrize("category", ["nieustalone", "prezenty", "inwestycje", "żywność", "alkohol"])
def test_old_or_unapproved_categories_are_rejected(category):
    raw = answer()
    raw["items"][0]["category"] = category
    with pytest.raises(ValueError):
        decode_enrichment(raw, inputs()[0])


def test_uncertain_category_is_null_with_separate_review_status():
    raw = answer()
    raw["items"][0].update(category=None, needs_review=True, review_reason="ambiguous_category")
    assert decode_enrichment(raw, inputs()[0])["items"][0]["category"] is None
    raw["items"][0]["needs_review"] = False
    with pytest.raises(ValueError):
        decode_enrichment(raw, inputs()[0])


def test_unreadable_name_cannot_be_silently_categorized():
    raw = answer()
    raw["items"][0].update(name_corrected=None, category=None, needs_review=True, review_reason="unreadable_name")
    assert decode_enrichment(raw, inputs()[0])["items"][0]["needs_review"]
    raw["items"][0]["category"] = "inne"
    with pytest.raises(ValueError):
        decode_enrichment(raw, inputs()[0])


def test_numeric_hallucination_is_replaced_by_raw_name_and_flagged():
    receipt, _ = inputs()
    receipt.items[0].name = "CHIPSY D0RITO 180g"
    raw = answer()
    raw["items"][0]["name_corrected"] = "CHIPSY DORITO 180g"
    assert not decode_enrichment(raw, receipt)["items"][0]["needs_review"]
    raw["items"][0]["name_corrected"] = "CHIPSY DORITO 200g"
    item = decode_enrichment(raw, receipt)["items"][0]
    assert item["name_corrected"] == "CHIPSY D0RITO 180g"
    assert item["needs_review"] and item["review_reason"] == "unsupported_name_change"


def test_all_categories_have_definitions_in_prompt():
    from extraction.categories import CATEGORIES, CATEGORY_DEFINITIONS
    assert len(CATEGORIES) == len(set(CATEGORIES)) == 18
    assert all(CATEGORY_DEFINITIONS.values())
    prompt = build_request(*inputs())["messages"][0]["content"]
    assert all(category in prompt for category in CATEGORIES)


def test_missing_raw_name_cannot_be_invented_by_model():
    receipt, _ = inputs()
    receipt.items[0].name = None
    item = decode_enrichment(answer(), receipt)["items"][0]
    assert item["name_corrected"] is item["category"] is None
    assert item["needs_review"] and item["review_reason"] == "unreadable_name"
