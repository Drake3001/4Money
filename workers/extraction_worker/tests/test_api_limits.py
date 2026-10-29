import asyncio
from io import BytesIO
import json
from types import SimpleNamespace

import pytest
from starlette.datastructures import UploadFile

import api_limits
from api_limits import RequestLimitsMiddleware, UploadLimitError, UploadLimits, read_upload


def limits(request=10, file=8):
    return UploadLimits(max_file_bytes=file, max_request_bytes=request)


def test_defaults_and_environment():
    defaults = UploadLimits.from_env({})
    assert defaults.max_file_bytes == 20 * 1024 * 1024
    assert defaults.max_request_bytes == 50 * 1024 * 1024
    actual = UploadLimits.from_env({"EXTRACTION_MAX_FILE_BYTES": "7",
                                   "EXTRACTION_MAX_REQUEST_BYTES": "9"})
    assert actual.max_file_bytes == 7 and actual.max_request_bytes == 9


@pytest.mark.parametrize("value", ["0", "-1", "", " ", "1.0", "nan", "+2"])
def test_invalid_environment_values(value):
    with pytest.raises(ValueError, match="positive integer"):
        UploadLimits.from_env({"EXTRACTION_MAX_FILE_BYTES": value})


def test_invalid_relations_fail_configuration():
    with pytest.raises(ValueError, match="max_file_bytes"):
        UploadLimits(max_file_bytes=11, max_request_bytes=10)
    with pytest.raises(ValueError, match="max_page_pixels"):
        UploadLimits(max_page_pixels=11, max_document_pixels=10)
    with pytest.raises(ValueError, match="positive integer"):
        UploadLimits(max_file_bytes=True)


def invoke(chunks, *, headers=(), configuration=None, path="/api/v1/extract",
           after=None, disconnect=False, app_state=False, error_response=None):
    sent, received = [], []
    messages = [{"type": "http.request", "body": chunk,
                 "more_body": index < len(chunks) - 1 or disconnect}
                for index, chunk in enumerate(chunks)]
    if disconnect:
        messages.append({"type": "http.disconnect"})
    scope = {"type": "http", "method": "POST", "path": path,
             "headers": headers, "state": {"request_id": "test-request"}}
    configuration = configuration or limits()
    if app_state:
        scope["app"] = SimpleNamespace(state=SimpleNamespace(upload_limits=configuration))

    async def app(scope, receive, send):
        while True:
            message = await receive()
            received.append(message)
            if not message.get("more_body", False):
                break
        if after:
            after()
        await send({"type": "http.response.start", "status": 200, "headers": []})
        await send({"type": "http.response.body", "body": b"ok"})

    async def receive():
        return messages.pop(0)

    async def send(message):
        sent.append(message)

    middleware = RequestLimitsMiddleware(app, limits=None if app_state else configuration,
                                         error_response=error_response)
    asyncio.run(middleware(scope, receive, send))
    return sent, received, messages


@pytest.mark.parametrize("body", [b"123456789", b"1234567890", b""])
def test_request_at_or_below_limit_is_replayed(body):
    sent, received, _ = invoke([body[:4], body[4:]])
    assert sent[0]["status"] == 200
    assert b"".join(message["body"] for message in received) == body
    assert received[-1]["more_body"] is False


@pytest.mark.parametrize("headers", [[], [(b"content-length", b"2")],
                                     [(b"content-length", b"invalid")]])
def test_actual_size_rejects_absent_false_or_invalid_length(headers):
    sent, received, remaining = invoke([b"123456", b"78901", b"unread"], headers=headers)
    assert sent[0]["status"] == 413 and received == []
    assert len(remaining) == 1
    assert json.loads(sent[1]["body"]) == {"error": {
        "code": "request_too_large", "message": "Request exceeds the upload limit.",
        "request_id": "test-request", "details": {"limit_bytes": 10}}}


@pytest.mark.parametrize("declared", [b"11", b"9" * 5000])
def test_declared_oversize_is_rejected_without_reading_body(declared):
    sent, received, remaining = invoke([b"unread"], headers=[(b"content-length", declared)])
    assert sent[0]["status"] == 413 and received == []
    assert len(remaining) == 1


def test_debug_uses_same_limit_and_application_settings():
    sent, received, _ = invoke([b"12345678901"], path="/api/v1/debug/extract", app_state=True)
    assert sent[0]["status"] == 413 and received == []


def test_unrelated_path_is_not_buffered_or_limited():
    sent, received, _ = invoke([b"12345678901"], path="/other")
    assert sent[0]["status"] == 200
    assert received[0]["body"] == b"12345678901"


@pytest.fixture
def spools(monkeypatch):
    opened = []
    original = api_limits.SpooledTemporaryFile

    def create(**kwargs):
        spool = original(**kwargs)
        opened.append(spool)
        return spool

    monkeypatch.setattr(api_limits, "SpooledTemporaryFile", create)
    return opened


def test_spooled_replay_chunking_and_cleanup(monkeypatch, spools):
    monkeypatch.setattr(api_limits, "SPOOL_MEMORY_BYTES", 10)
    body = b"x" * (api_limits.CHUNK_BYTES * 2 + 1)
    configuration = limits(request=len(body), file=8)
    sent, received, _ = invoke([body], configuration=configuration)
    assert sent[0]["status"] == 200
    assert [len(message["body"]) for message in received] == [65536, 65536, 1]
    assert [message["more_body"] for message in received] == [True, True, False]
    assert spools[0]._rolled and spools[0].closed


@pytest.mark.parametrize("disconnect", [False, True])
def test_spool_closed_on_rejection_or_disconnect(spools, disconnect):
    sent, received, _ = invoke([b"x" if disconnect else b"x" * 11], disconnect=disconnect)
    assert received == [] and spools[0].closed
    assert sent == [] if disconnect else sent[0]["status"] == 413


def test_spool_closed_on_application_failure(spools):
    def fail():
        raise RuntimeError("Synthetic downstream failure")

    with pytest.raises(RuntimeError, match="Synthetic downstream failure"):
        invoke([b"body"], after=fail)
    assert spools[0].closed


def test_common_error_factory_receives_code_id_and_safe_details():
    calls = []

    def render(code, request_id, details):
        calls.append((code, request_id, details))
        return api_limits._limit_response(code, request_id, details)

    sent, _, _ = invoke([b"x" * 11], error_response=render)
    assert sent[0]["status"] == 413
    assert calls == [("request_too_large", "test-request", {"limit_bytes": 10})]


def test_per_file_limit_and_exact_boundary():
    async def run():
        file = UploadFile(BytesIO(b"12345678"))
        assert await read_upload(file, limits(), 1) == b"12345678"
        await file.close()
        file = UploadFile(BytesIO(b"123456789"))
        with pytest.raises(UploadLimitError) as caught:
            await read_upload(file, limits(), 2)
        assert caught.value.code == "file_too_large"
        assert caught.value.status_code == 413
        assert caught.value.details == {"file_index": 2, "limit_bytes": 8}
        await file.close()
    asyncio.run(run())


def test_per_file_reads_stop_after_first_exceeding_chunk():
    class File:
        reads = 0

        async def read(self, size):
            assert size == api_limits.CHUNK_BYTES
            self.reads += 1
            return b"x" * size

    file = File()
    with pytest.raises(UploadLimitError):
        asyncio.run(read_upload(file, limits(), 1))
    assert file.reads == 1
