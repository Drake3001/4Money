"""Exercise the new boundary between receipt tasks and provider transports."""

import json
from io import BytesIO
from urllib.error import HTTPError, URLError

import pytest

from extraction.analysis import ReceiptImageAnalyzer, ReceiptItemClassifier
from extraction.providers import GoogleClient, NvidiaClient, OpenRouterClient, ProviderHTTPError
from extraction.providers.http import NoRedirect
from extraction.steps.analysis import ClassifyReceiptItemsStep
from extraction.steps.parsing import GeminiParseStep
from test_gemma import answer, inputs
from test_gemma_vision import response as image_response, source
from test_gemini import response as google_response
from test_parsing import at, document, ocr


def envelope(raw):
    return {"choices": [{"finish_reason": "stop", "message": {"content": json.dumps(raw)}}]}


def test_one_openrouter_transport_serves_both_receipt_tasks(monkeypatch):
    requests = []

    class Opener:
        def open(self, request, timeout):
            requests.append((request, timeout))
            raw = answer() if timeout == 60 else image_response()
            return BytesIO(json.dumps(envelope(raw)).encode())

    monkeypatch.setattr("extraction.providers.http.build_opener", lambda handler: Opener())
    transport = OpenRouterClient("test-key")
    classifier = ReceiptItemClassifier(transport, allow_paid=True)
    analyzer = ReceiptImageAnalyzer(transport, allow_paid=True)
    assert classifier.run(*inputs())["items"]
    assert analyzer.run([source()]).output.items
    assert [timeout for _, timeout in requests] == [60, 180]
    assert {request.full_url for request, _ in requests} == {"https://openrouter.ai/api/v1/chat/completions"}
    assert all(request.get_header("Authorization") == "Bearer test-key" for request, _ in requests)
    assert not hasattr(transport, "cache_dir") and not hasattr(transport, "prompt")
    config = ClassifyReceiptItemsStep(classifier).config
    assert (config["task"], config["client"], config["provider"]) == (
        "ReceiptItemClassifier", "OpenRouterClient", "openrouter")


def test_google_transport_is_shared_by_gemma_classification_and_gemini_parsing(monkeypatch):
    requests = []

    class Opener:
        def open(self, request, timeout):
            requests.append((request.full_url, timeout))
            raw = (google_response() if timeout == 90 else
                   {"candidates": [{"finishReason": "STOP", "content": {"parts": [
                       {"text": "hidden reasoning", "thought": True}, {"text": json.dumps(answer())}]}}]})
            return BytesIO(json.dumps(raw).encode())

    monkeypatch.setattr("extraction.providers.http.build_opener", lambda handler: Opener())
    transport = GoogleClient("test-key")
    assert ReceiptItemClassifier(transport).run(*inputs())["items"]
    parser = GeminiParseStep(transport)
    parser.run(ocr(document(at("Napój 1 x3,95 3,95A", 0, 45))))
    assert requests == [
        ("https://generativelanguage.googleapis.com/v1beta/models/gemma-4-31b-it:generateContent", 60),
        ("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent", 90),
    ]


@pytest.mark.parametrize("client", [OpenRouterClient, GoogleClient, NvidiaClient])
@pytest.mark.parametrize("status", [302, 429, 500])
def test_transports_do_not_retry_redirect_or_expose_provider_bodies(client, status, monkeypatch):
    calls = []

    class Opener:
        def open(self, request, timeout):
            calls.append(request)
            raise HTTPError(request.full_url, status, "secret-body test-key", {}, BytesIO(b"secret-body test-key"))

    def opener(handler):
        assert isinstance(handler, NoRedirect)
        return Opener()

    monkeypatch.setattr("extraction.providers.http.build_opener", opener)
    transport = client("test-key")
    options = {"model": "gemma-4-31b-it"} if client is GoogleClient else {}
    with pytest.raises(ProviderHTTPError) as error:
        transport.send(b"{}", **options)
    assert error.value.status == status and len(calls) == 1
    assert "test-key" not in str(error.value) and "secret-body" not in str(error.value)


@pytest.mark.parametrize("failure", [TimeoutError("test-key"), URLError("test-key")])
def test_network_errors_do_not_expose_credentials_or_retry(failure, monkeypatch):
    calls = []

    class Opener:
        def open(self, request, timeout):
            calls.append(request)
            raise failure

    monkeypatch.setattr("extraction.providers.http.build_opener", lambda handler: Opener())
    with pytest.raises(RuntimeError) as error:
        OpenRouterClient("test-key").send(b"{}")
    assert len(calls) == 1 and "test-key" not in str(error.value)


def test_paid_access_is_task_configuration_and_requires_opt_in():
    transport = OpenRouterClient("test-key")
    free = ReceiptItemClassifier(transport)
    assert free.model.endswith(":free") and not free.allow_paid
    with pytest.raises(ValueError):
        ReceiptItemClassifier(transport, model="google/gemma-4-31b-it")
    with pytest.raises(ValueError):
        ReceiptImageAnalyzer(transport)
    for client in (GoogleClient, NvidiaClient):
        with pytest.raises(ValueError):
            ReceiptItemClassifier(client("test-key"), allow_paid=True)
        with pytest.raises(ValueError):
            ReceiptImageAnalyzer(client("test-key"), allow_paid=True)
    for invalid in (True, 255, 8193):
        with pytest.raises(ValueError):
            ReceiptItemClassifier(transport, allow_paid=True, max_output_tokens=invalid)
    assert ReceiptItemClassifier(transport, allow_paid=True).model == ReceiptImageAnalyzer(
        transport, allow_paid=True).model
