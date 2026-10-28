"""Exercise the HTTP boundary with real preparation and substituted OCR/provider."""

from io import BytesIO
import json

from fastapi.testclient import TestClient
from PIL import Image
from pypdf import PdfWriter
import pytest

import api
from extraction.workflows import service_pipelines
from test_gemma_vision import response as vision_response


def pdf_bytes(pages):
    writer = PdfWriter()
    for _ in range(pages):
        writer.add_blank_page(width=72, height=144)
    buffer = BytesIO()
    writer.write(buffer)
    return buffer.getvalue()


def picture(fmt):
    if fmt == "HEIF":
        from pillow_heif import register_heif_opener
        register_heif_opener()
    buffer = BytesIO()
    Image.new("RGB", (20, 30), "white").save(buffer, format=fmt)
    return buffer.getvalue()


@pytest.fixture
def integrated(monkeypatch):
    monkeypatch.setenv("OPENROUTER_API_KEY", "test-key")
    monkeypatch.delenv("EXTRACTION_WORKFLOW", raising=False)
    monkeypatch.setattr(api, "warmup_service", lambda pipelines: None)
    requests, ocr_calls = [], []

    def build(classifier, analyzer, **kwargs):
        def policy(body, send):
            requests.append(json.loads(body))
            return {"choices": [{"finish_reason": "stop", "message": {"content": json.dumps(vision_response())}}]}

        analyzer.request_policy = policy
        pipelines = service_pipelines(classifier, analyzer, **kwargs)
        steps = dict(pipelines["vision_ocr"].steps)
        monkeypatch.setattr(steps["orient"], "_predict", lambda image: (0, 0.99))
        monkeypatch.setattr(steps["ocr"], "initialize", lambda: None)
        monkeypatch.setattr(steps["ocr"], "recognize", lambda content: ocr_calls.append(content) or [])
        return pipelines

    monkeypatch.setattr(api, "service_pipelines", build)
    with TestClient(api.app) as client:
        yield client, requests, ocr_calls


def assert_error(response, status, code):
    assert response.status_code == status, response.text
    error = response.json()["error"]
    assert set(error) == {"code", "message", "request_id", "details"}
    assert error["code"] == code
    assert error["request_id"] == response.headers["x-request-id"]
    assert len(error["request_id"]) == 32


@pytest.mark.parametrize("fmt", ["JPEG", "PNG", "WEBP", "HEIF", "TIFF", "BMP"])
def test_formats_are_detected_from_content(integrated, fmt):
    client, requests, ocr_calls = integrated
    response = client.post("/api/v1/extract", files={"files": ("misleading.txt", picture(fmt), "application/octet-stream")})
    assert response.status_code == 200, response.text
    assert len(requests) == len(ocr_calls) == 1
    assert requests[0]["model"] == "google/gemini-3.1-flash-lite"
    assert response.headers["x-request-id"]


def test_pdf_and_photo_keep_four_pages_in_one_model_call(integrated):
    client, requests, ocr_calls = integrated
    response = client.post("/api/v1/debug/extract", files=[
        ("files", ("document.pdf", pdf_bytes(3), "application/pdf")),
        ("files", ("photo.png", picture("PNG"), "image/png")),
    ])
    assert response.status_code == 200, response.text
    assert len(ocr_calls) == 4 and len(requests) == 1
    pages = response.json()["ocr"]["pages"]
    assert [p["page_number"] for p in pages] == [1, 2, 3, 4]
    assert [p["source_page_number"] for p in pages] == [1, 2, 3, 1]


@pytest.mark.parametrize("endpoint", ["/api/v1/extract", "/api/v1/debug/extract"])
def test_fifth_page_rejected_before_ocr(integrated, endpoint):
    client, requests, ocr_calls = integrated
    response = client.post(endpoint, files={"files": ("receipt.pdf", pdf_bytes(5), "application/pdf")})
    assert_error(response, 422, "too_many_pages")
    assert not requests and not ocr_calls


def test_unknown_format_and_corrupt_supported_file(integrated):
    client, requests, ocr_calls = integrated
    assert_error(client.post("/api/v1/extract", files={"files": ("a.jpg", b"plain text", "image/jpeg")}),
                 415, "unsupported_format")
    assert_error(client.post("/api/v1/extract", files={"files": ("a.png", b"\x89PNG\r\n\x1a\ncorrupt", "image/png")}),
                 422, "invalid_file")
    assert not requests and not ocr_calls


def test_file_and_request_limits_apply_before_pipeline(integrated):
    from api_limits import UploadLimits
    client, requests, ocr_calls = integrated
    api.app.state.upload_limits = UploadLimits(max_file_bytes=10, max_request_bytes=1024)
    assert_error(client.post("/api/v1/extract", files={"files": ("a.png", b"x" * 11, "image/png")}),
                 413, "file_too_large")
    api.app.state.upload_limits = UploadLimits(max_file_bytes=10, max_request_bytes=20)
    assert_error(client.post("/api/v1/debug/extract", files={"files": ("a.png", b"x", "image/png")}),
                 413, "request_too_large")
    assert not requests and not ocr_calls


def test_fastapi_errors_share_request_ids_and_hide_input(integrated):
    client, _, _ = integrated
    assert_error(client.post("/api/v1/extract"), 422, "validation_error")
    response = client.post("/api/v1/debug/extract?workflow=PRIVATE-INPUT", files={"files": ("a.png", picture("PNG"), "image/png")})
    assert_error(response, 422, "validation_error")
    assert "PRIVATE-INPUT" not in response.text
    assert_error(client.get("/missing"), 404, "not_found")
    assert_error(client.get("/api/v1/extract"), 405, "method_not_allowed")


def test_bad_limit_configuration_stops_startup(monkeypatch):
    monkeypatch.setenv("EXTRACTION_MAX_FILE_BYTES", "0")
    with pytest.raises(ValueError, match="EXTRACTION_MAX_FILE_BYTES"):
        with TestClient(api.app):
            pass


def test_pixel_limits_are_mapped_to_413_before_ocr(integrated):
    from extraction.contracts import PreparationConfig
    client, requests, ocr_calls = integrated
    prepare = dict(api.app.state.pipelines["vision_ocr"].steps)["prepare"]
    prepare.settings = PreparationConfig(max_pages=4, max_page_pixels=599)
    assert_error(client.post("/api/v1/extract", files={"files": ("a.png", picture("PNG"), "image/png")}),
                 413, "image_too_large")
    assert not requests and not ocr_calls


@pytest.mark.parametrize("code,status", [
    ("provider_timeout", 504), ("provider_network_error", 502),
    ("provider_unavailable", 503), ("provider_response_invalid", 502),
])
@pytest.mark.parametrize("endpoint", ["/api/v1/extract", "/api/v1/debug/extract"])
def test_provider_failures_have_safe_responses(integrated, code, status, endpoint):
    from extraction.contracts import PipelineError, PipelineResult
    client, _, _ = integrated
    result = PipelineResult(error=PipelineError("analyze", "RuntimeError", "PRIVATE-RECEIPT-OR-KEY", code))
    api.app.state.pipelines["vision_ocr"].run = lambda *args, **kwargs: result
    response = client.post(endpoint, files={"files": ("a.png", picture("PNG"), "image/png")})
    assert_error(response, status, code)
    assert "PRIVATE-RECEIPT-OR-KEY" not in response.text


def test_unhandled_exceptions_are_not_rethrown_into_server_logs(integrated, caplog):
    client, _, _ = integrated

    def fail(*args, **kwargs):
        raise RuntimeError("PRIVATE-RECEIPT-OR-KEY")

    api.app.state.pipelines["vision_ocr"].run = fail
    # A response must be returned even when the test client would rethrow errors.
    response = client.post("/api/v1/extract", files={"files": ("a.png", picture("PNG"), "image/png")})
    assert_error(response, 500, "internal_error")
    assert "PRIVATE-RECEIPT-OR-KEY" not in response.text + caplog.text
