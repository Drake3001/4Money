import asyncio
from concurrent.futures import ThreadPoolExecutor
from decimal import Decimal
from datetime import date
from io import BytesIO
from threading import Event, Lock
from types import SimpleNamespace

import pytest
from fastapi.testclient import TestClient
from PIL import Image

import api
from extraction.contracts import (
    AnalyzedReceipt, ItemAnnotation, PipelineError, PipelineResult,
    ReceiptData, ReceiptItem, SourceFiles, SourceFile, ExtractionWarning, OCRResult,
)
from extraction.pipeline import Pipeline
from extraction.reporting import to_dict
from extraction.steps.prepare import PrepareDocumentStep


def image_bytes():
    buffer = BytesIO()
    Image.new("RGB", (10, 10), "white").save(buffer, format="PNG")
    return buffer.getvalue()


def classified_result():
    receipt = ReceiptData(date=date(2026, 10, 7), items=[ReceiptItem(name="MLEKO", total=Decimal("3.50"))])
    return PipelineResult(output=AnalyzedReceipt(receipt, (
        ItemAnnotation(0, "MLEKO", "Mleko", None, True, "ambiguous_category"),
    )))


@pytest.fixture(autouse=True)
def no_real_warmup(monkeypatch):
    monkeypatch.setattr(api, "warmup_service", lambda pipelines: None)


@pytest.fixture
def client(monkeypatch):
    monkeypatch.setenv("OPENROUTER_API_KEY", "test_key")
    monkeypatch.delenv("EXTRACTION_WORKFLOW", raising=False)
    monkeypatch.setattr(api, "service_pipelines", lambda *args: {
        name: SimpleNamespace(run=lambda sources, **kwargs: classified_result())
        for name in api.WORKFLOW_NAMES
    })
    with TestClient(api.app, raise_server_exceptions=False) as client:
        yield client


def test_startup_requires_key(monkeypatch):
    monkeypatch.delenv("OPENROUTER_API_KEY", raising=False)
    with pytest.raises(ValueError, match="OPENROUTER_API_KEY"):
        with TestClient(api.app):
            pass


def test_startup_uses_paid_classification_without_cache(monkeypatch):
    monkeypatch.setenv("OPENROUTER_API_KEY", "test_key")
    classifiers = []

    def build(classifier, analyzer):
        classifiers.extend([classifier, analyzer])
        return {name: SimpleNamespace(run=lambda sources, **kwargs: classified_result())
                for name in api.WORKFLOW_NAMES}

    monkeypatch.delenv("EXTRACTION_WORKFLOW", raising=False)
    monkeypatch.setattr(api, "service_pipelines", build)
    with TestClient(api.app) as client:
        assert client.get("/health").json() == {"status": "ok"}
        assert client.get("/docs").status_code == 200
    assert len(classifiers) == 2
    assert all(task.allow_paid and task.cache_dir is None for task in classifiers)
    assert api.app.state.workflow == "vision_ocr"
    assert classifiers[0].allow_paid is True
    assert classifiers[0].cache_dir is None
    assert classifiers[0].model == "google/gemma-4-31b-it"


def test_startup_waits_for_warmup_once(client, monkeypatch):
    warmed = []
    monkeypatch.setattr(api, "warmup_service", lambda pipelines: warmed.append(pipelines))
    with TestClient(api.app) as other:
        assert len(warmed) == 1
        assert warmed[0] is api.app.state.pipelines
        assert other.get("/health").status_code == 200


def test_warmup_failure_prevents_startup(client, monkeypatch):
    def fail(pipelines):
        raise RuntimeError("Warmup failed")
    monkeypatch.setattr(api, "warmup_service", fail)
    with pytest.raises(RuntimeError, match="Warmup failed"):
        with TestClient(api.app):
            pass


@pytest.mark.parametrize("endpoint", ["/api/v1/extract", "/api/v1/debug/extract"])
def test_openapi_describes_multipart_files_for_swagger_picker(client, endpoint):
    schema = client.get("/openapi.json").json()
    body = schema["paths"][endpoint]["post"]["requestBody"]
    reference = body["content"]["multipart/form-data"]["schema"]["$ref"]
    component = schema["components"]["schemas"][reference.rsplit("/", 1)[1]]
    files = component["properties"]["files"]
    assert "files" in component["required"]
    assert files["type"] == "array"
    assert files["items"] == {"type": "string", "format": "binary"}


def test_upload_keeps_bytes_and_order_and_returns_existing_contract(client):
    received = []
    result = classified_result()

    def run(sources, **kwargs):
        received.append(sources)
        return result

    api.app.state.pipelines[api.app.state.workflow].run = run
    response = client.post("/api/v1/extract", files=[
        ("files", ("02.png", b"first", "image/png")),
        ("files", ("01.jpg", b"second", "image/jpeg")),
    ])
    assert response.status_code == 200
    assert response.json() == api.application_response(result).model_dump(mode="json")
    assert response.json()["receipt"]["items"][0]["total"] == "3.50"
    assert response.json()["receipt"]["date"] == "2026-10-07"
    assert response.json()["receipt"]["items"][0]["needs_review"] is True
    assert isinstance(received[0], SourceFiles)
    assert [(f.name, f.content) for f in received[0].files] == [
        ("02.png", b"first"), ("01.jpg", b"second"),
    ]


@pytest.mark.parametrize("files,status", [
    ([], 422),
    ([("files", ("empty.png", b"", "image/png"))], 422),
    ([("files", ("receipt.pdf", b"pdf", "application/pdf"))], 415),
])
def test_invalid_uploads(client, files, status):
    assert client.post("/api/v1/extract", files=files).status_code == status


def test_real_preparation_rejects_corrupt_image(client):
    api.app.state.pipelines[api.app.state.workflow] = Pipeline([("prepare", PrepareDocumentStep())])
    response = client.post("/api/v1/extract", files={
        "files": ("broken.png", b"broken", "image/png"),
    })
    assert response.status_code == 422
    assert response.json()["error"]["step"] == "prepare"


@pytest.mark.parametrize("step,kind,status", [
    ("prepare", "PreparationError", 422),
    ("prepare", "RuntimeError", 500),
    ("classify", "ValueError", 500),
])
def test_pipeline_errors_exclude_partial_output(client, step, kind, status):
    error = PipelineError(step, kind, "Cannot process receipt")
    api.app.state.pipelines[api.app.state.workflow].run = lambda sources, **kwargs: PipelineResult(output="partial", error=error)
    response = client.post("/api/v1/extract", files={
        "files": ("receipt.png", image_bytes(), "image/png"),
    })
    assert response.status_code == status
    assert response.json() == {"error": to_dict(error)}


def test_unexpected_error_is_generic_and_lock_is_released(client):
    def fail(sources, **kwargs):
        raise RuntimeError("private exception details")

    api.app.state.pipelines[api.app.state.workflow].run = fail
    upload = {"files": ("receipt.png", image_bytes(), "image/png")}
    response = client.post("/api/v1/extract", files=upload)
    assert response.status_code == 500
    assert response.json() == {"error": {"message": "Internal server error"}}
    api.app.state.pipelines[api.app.state.workflow].run = lambda sources, **kwargs: classified_result()
    assert client.post("/api/v1/extract", files=upload).status_code == 200


def test_concurrent_requests_are_serial_and_health_stays_responsive(client):
    entered, release, second_queued = Event(), Event(), Event()
    calls, active, maximum = 0, 0, 0
    state_lock = Lock()
    original_lock = api.app.state.pipeline_lock

    class ObservedLock:
        def __enter__(self):
            if original_lock.locked():
                second_queued.set()
            original_lock.acquire()

        def __exit__(self, *args):
            original_lock.release()

    api.app.state.pipeline_lock = ObservedLock()

    def run(sources, **kwargs):
        nonlocal calls, active, maximum
        with state_lock:
            calls += 1
            active += 1
            maximum = max(maximum, active)
        entered.set()
        assert release.wait(10)
        with state_lock:
            active -= 1
        return classified_result()

    for pipeline in api.app.state.pipelines.values():
        pipeline.run = run

    def post():
        return client.post("/api/v1/extract", files={
            "files": ("receipt.png", image_bytes(), "image/png"),
        })

    with ThreadPoolExecutor(max_workers=3) as pool:
        first = pool.submit(post)
        try:
            assert entered.wait(5)
            second = pool.submit(lambda: client.post("/api/v1/debug/extract?workflow=text", files={
                "files": ("receipt.png", image_bytes(), "image/png"),
            }))
            assert second_queued.wait(5)
            assert pool.submit(client.get, "/health").result(timeout=5).status_code == 200
            assert calls == 1
        finally:
            release.set()
        assert first.result(timeout=5).status_code == 200
        assert second.result(timeout=5).status_code == 200
    assert calls == 2 and maximum == 1


def test_cancelling_awaiter_does_not_release_execution_lock():
    entered, release = Event(), Event()
    lock = Lock()

    def run(sources, **kwargs):
        entered.set()
        assert release.wait(10)
        return classified_result()

    app = SimpleNamespace(state=SimpleNamespace(pipeline_lock=lock, pipelines={"vision_ocr": SimpleNamespace(run=run)}))

    async def scenario():
        task = asyncio.create_task(asyncio.to_thread(api.execute_pipeline, app, None, "test", "vision_ocr"))
        try:
            assert await asyncio.to_thread(entered.wait, 5)
            task.cancel()
            with pytest.raises(asyncio.CancelledError):
                await task
            assert lock.locked()
        finally:
            release.set()

    asyncio.run(scenario())
    assert not lock.locked()


def test_debug_keeps_diagnostics_and_selects_only_one_pipeline(client):
    calls = []
    result = classified_result()
    for name, pipeline in api.app.state.pipelines.items():
        def run(sources, *, capture_steps, selected=name):
            calls.append((selected, capture_steps))
            return result
        pipeline.run = run
    response = client.post("/api/v1/debug/extract?workflow=text", files={
        "files": ("receipt.png", image_bytes(), "image/png"),
    })
    assert response.status_code == 200
    body = response.json()
    assert body["output"] == to_dict(result.output)
    assert body["workflow"] == "text" and body["ocr"] is None
    assert body["timing"]["wait_seconds"] >= 0
    assert body["timing"]["pipeline_seconds"] >= 0
    assert calls == [("text", ("ocr",))]


def test_response_has_no_diagnostics_and_warnings_trigger_review(client):
    result = classified_result()
    result.output = AnalyzedReceipt(ReceiptData(), ())
    result.warnings.append(ExtractionWarning("missing_field", "Missing date", "date"))
    api.app.state.pipelines[api.app.state.workflow].run = lambda *args, **kwargs: result
    response = client.post("/api/v1/extract", files={"files": ("a.png", b"png", "image/png")})
    body = response.json()
    assert set(body) == {"receipt", "warnings", "needs_review"}
    assert body["needs_review"] is True
    assert "evidence" not in body["receipt"]
    assert api.application_response(PipelineResult(output=AnalyzedReceipt(ReceiptData(), ()))).needs_review is False


@pytest.mark.parametrize("endpoint", ["/api/v1/extract", "/api/v1/debug/extract"])
def test_file_count_limit_before_pipeline(client, endpoint):
    def fail(*args, **kwargs):
        pytest.fail("No pipeline should execute")
    for pipeline in api.app.state.pipelines.values():
        pipeline.run = fail
    assert client.post(endpoint, files=[("files", ("a.png", b"a", "image/png"))] * 5).status_code == 422


def test_invalid_debug_workflow(client):
    assert client.post("/api/v1/debug/extract?workflow=unknown", files={
        "files": ("a.png", b"a", "image/png"),
    }).status_code == 422


def test_invalid_env_workflow_stops_startup(monkeypatch):
    monkeypatch.setenv("EXTRACTION_WORKFLOW", "unknown")
    with pytest.raises(ValueError, match="EXTRACTION_WORKFLOW"):
        with TestClient(api.app):
            pass


def test_env_workflow_is_used_by_both_endpoints(monkeypatch):
    monkeypatch.setenv("OPENROUTER_API_KEY", "test_key")
    monkeypatch.setenv("EXTRACTION_WORKFLOW", "vision")
    calls = []
    def run(*args, **kwargs):
        calls.append("vision")
        return classified_result()
    monkeypatch.setattr(api, "service_pipelines", lambda *args: {"vision": SimpleNamespace(run=run)})
    with TestClient(api.app) as client:
        for endpoint in ("/api/v1/extract", "/api/v1/debug/extract"):
            assert client.post(endpoint, files={"files": ("a.png", b"a", "image/png")}).status_code == 200
    assert calls == ["vision", "vision"]


def test_debug_failure_omits_partial_receipt(client):
    result = PipelineResult(output="partial", error=PipelineError("analyze", "ValueError", "Bad reply"))
    api.app.state.pipelines[api.app.state.workflow].run = lambda *args, **kwargs: result
    response = client.post("/api/v1/debug/extract", files={"files": ("a.png", b"a", "image/png")})
    assert response.status_code == 500
    assert "output" not in response.json()
    assert response.json()["error"] == to_dict(result.error)


def test_debug_returns_ocr_without_source_bytes(client):
    image = image_bytes()
    document = PrepareDocumentStep().run(SourceFiles((SourceFile(image, "image/png", "a.png"),))).output
    ocr = OCRResult(document, (), "rapidocr", "test", {})
    result = classified_result()
    result.captured_steps["ocr"] = ocr
    api.app.state.pipelines[api.app.state.workflow].run = lambda *args, **kwargs: result
    response = client.post("/api/v1/debug/extract", files={"files": ("a.png", image, "image/png")})
    assert response.status_code == 200
    body = response.json()
    assert body["ocr"] == to_dict(ocr)
    assert body["ocr"]["engine"] == "rapidocr"
    assert "content" not in body["ocr"]["document"]["sources"][0]
    assert "content" not in body["ocr"]["document"]["pages"][0]
