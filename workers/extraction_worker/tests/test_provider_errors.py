from io import BytesIO
from urllib.error import HTTPError, URLError

import pytest

from extraction.analysis import ReceiptImageAnalyzer, ReceiptItemClassifier
from extraction.providers import (
    OpenRouterClient, ProviderHTTPError, ProviderNetworkError, ProviderResponseError, ProviderTimeoutError,
)
from test_gemma import inputs
from test_gemma_vision import source


@pytest.mark.parametrize("failure,kind,code", [
    (TimeoutError("secret"), ProviderTimeoutError, "provider_timeout"),
    (URLError(TimeoutError("secret")), ProviderTimeoutError, "provider_timeout"),
    (URLError("secret"), ProviderNetworkError, "provider_network_error"),
    (ConnectionResetError("secret"), ProviderNetworkError, "provider_network_error"),
    (HTTPError("private", 429, "secret", {}, None), ProviderHTTPError, "provider_unavailable"),
    (HTTPError("private", 503, "secret", {}, None), ProviderHTTPError, "provider_unavailable"),
    (HTTPError("private", 500, "secret", {}, None), ProviderHTTPError, "provider_error"),
])
def test_transport_preserves_error_kind_without_retry_or_provider_details(monkeypatch, failure, kind, code):
    calls = []

    class Opener:
        def open(self, request, timeout):
            calls.append(request)
            raise failure

    monkeypatch.setattr("extraction.providers.http.build_opener", lambda handler: Opener())
    with pytest.raises(kind) as caught:
        OpenRouterClient("test-key").send(b"{}")
    assert caught.value.code == code
    assert "secret" not in str(caught.value) and "private" not in str(caught.value)
    assert len(calls) == 1


@pytest.mark.parametrize("body", [b"secret-not-json", b"\xff"])
def test_invalid_provider_json_is_typed_and_sanitized(monkeypatch, body):
    class Opener:
        def open(self, request, timeout):
            return BytesIO(body)

    monkeypatch.setattr("extraction.providers.http.build_opener", lambda handler: Opener())
    with pytest.raises(ProviderResponseError) as caught:
        OpenRouterClient("test-key").send(b"{}")
    assert caught.value.code == "provider_response_invalid"
    assert "secret" not in str(caught.value)


@pytest.mark.parametrize("failure", [ProviderTimeoutError("safe"), ProviderNetworkError("safe")])
def test_image_analyzer_does_not_erase_transport_error_kind(monkeypatch, failure):
    client = OpenRouterClient("test-key")

    def send(*args, **kwargs):
        raise failure

    monkeypatch.setattr(client, "send", send)
    with pytest.raises(type(failure)):
        ReceiptImageAnalyzer(client, allow_paid=True).run([source()])


@pytest.mark.parametrize("response", [None, [], {"choices": None}, {"choices": [{}]},
    {"choices": [{"finish_reason": "stop", "message": {"content": "secret-not-json"}}]},
    {"choices": [{"finish_reason": "stop", "message": {"content": "{}"}}]},
])
@pytest.mark.parametrize("task", ["image", "classifier"])
def test_invalid_model_envelopes_or_receipt_schema_are_provider_errors(task, response):
    client = OpenRouterClient("test-key")
    policy = lambda body, send: response
    with pytest.raises(ProviderResponseError) as caught:
        if task == "image":
            ReceiptImageAnalyzer(client, allow_paid=True, request_policy=policy).run([source()])
        else:
            ReceiptItemClassifier(client, allow_paid=True, request_policy=policy).run(*inputs())
    assert "secret" not in str(caught.value)
