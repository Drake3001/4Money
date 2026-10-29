import copy
import json

import pytest

from experiments import latency


def test_compact_preserves_all_ocr_values_and_does_not_mutate_input():
    pages = [{"page": 1, "fragments": [{"id": "p1:f0", "text": "Żółty 0,50",
              "confidence": 0.751, "box": [[1, 2], [3, 4], [5, 6], [7, 8]]}]}]
    original = {"provider": {"order": ["deepinfra"], "max_price": {"prompt": 0.3}},
                "messages": [{"role": "system", "content": "frozen prompt"}, {"content": [
                    {"type": "text", "text": latency.OCR_GUIDANCE + json.dumps(pages)}]}]}
    snapshot = copy.deepcopy(original)
    changed = latency.variant_request(original, "compact")
    assert original == snapshot
    assert changed["messages"][0] == original["messages"][0]
    assert changed["provider"] == {"sort": "throughput", "max_price": {"prompt": 0.3}}
    text = changed["messages"][1]["content"][-1]["text"]
    compact = json.loads(text.split("\n")[-1])
    f = pages[0]["fragments"][0]
    assert compact == [{"page": 1, "fragments": [[f["id"], f["text"], f["confidence"], f["box"]]]}]


def fake_stream(monkeypatch, events, done=True):
    class Stream:
        def __enter__(self):
            return self

        def __exit__(self, *args):
            pass

        def __iter__(self):
            yield b": heartbeat\n"
            for event in events:
                yield ("data: " + json.dumps(event) + "\n").encode()
            if done:
                yield b"data: [DONE]\n"

    class Opener:
        def open(self, request, timeout):
            assert request.full_url == latency.BASE_URL + "/chat/completions"
            return Stream()

    monkeypatch.setattr(latency, "build_opener", lambda *args: Opener())


def test_sse_keeps_usage_after_finish_and_measures_content_separately(monkeypatch):
    fake_stream(monkeypatch, [
        {"id": "gen-test", "provider": "Test", "choices": [{"delta": {"reasoning": "private"}}]},
        {"choices": [{"delta": {"content": "{\"ok\":"}}]},
        {"choices": [{"delta": {"content": "true}"}, "finish_reason": "stop"}]},
        {"choices": [], "usage": {"cost": 0.001, "completion_tokens": 5}},
    ])
    response, timing = latency.stream_response({}, "test-key")
    assert response["choices"][0]["message"]["content"] == '{"ok":true}'
    assert response["usage"]["cost"] == 0.001
    assert timing["stream_done"]
    assert timing["first_reasoning_seconds"] <= timing["first_content_seconds"]
    assert "private" not in json.dumps(response)


def test_abrupt_eof_is_not_a_complete_stream(monkeypatch):
    fake_stream(monkeypatch, [{"choices": [{"delta": {"content": "{}"}, "finish_reason": "stop"}]}], done=False)
    _, timing = latency.stream_response({}, "test-key")
    assert not timing["stream_done"]


def test_sse_error_is_rejected_without_exposing_provider_body(monkeypatch):
    fake_stream(monkeypatch, [{"error": {"message": "sensitive"}}])
    with pytest.raises(RuntimeError, match="SSE error") as error:
        latency.stream_response({}, "test-key")
    assert "sensitive" not in str(error.value)


def test_sse_failure_retains_usage_and_id_but_redacts_credentials(monkeypatch):
    fake_stream(monkeypatch, [
        {"id": "gen-failed", "provider": "Test", "choices": [{"delta": {"content": "partial"}}]},
        {"error": {"code": 502, "message": "bad test-secret sk-provider-key"},
         "usage": {"cost": 0.002}},
    ])
    with pytest.raises(latency.StudyStreamError) as error:
        latency.stream_response({}, "test-secret")
    assert error.value.response["id"] == "gen-failed"
    assert error.value.response["usage"]["cost"] == 0.002
    assert error.value.response["choices"][0]["message"]["content"] == "partial"
    assert not error.value.timing["stream_done"]
    assert "test-secret" not in json.dumps(error.value.response)
    assert "sk-provider-key" not in json.dumps(error.value.response)
