import json

import pytest

from api_errors import APIError, ERRORS, error_response, pipeline_error
from api_models import ErrorResponse
from extraction.contracts import PipelineError
from extraction.pipeline import Pipeline
from extraction.providers import ProviderTimeoutError


@pytest.mark.parametrize("code", ERRORS)
def test_error_codes_have_one_validated_public_envelope(code):
    response = error_response(code, "request-123")
    payload = json.loads(response.body)
    ErrorResponse.model_validate(payload)
    assert response.status_code == ERRORS[code][0]
    assert response.headers["x-request-id"] == "request-123"
    assert payload == {"error": {
        "code": code, "message": ERRORS[code][1], "request_id": "request-123", "details": None,
    }}


def test_pipeline_errors_preserve_codes_but_never_exception_text_or_arbitrary_details():
    failure = PipelineError("analyze", "ProviderTimeoutError", "secret receipt/API key/path", "provider_timeout", {
        "file_index": 2, "limit_bytes": 100, "filename": "private.jpg", "response": "secret", "step": ["secret"],
    })
    status, payload = pipeline_error(failure, "request-123")
    assert status == 504
    assert payload["error"]["details"] == {"file_index": 2, "limit_bytes": 100, "step": "analyze"}
    assert "secret" not in json.dumps(payload)
    assert "ProviderTimeoutError" not in json.dumps(payload)


@pytest.mark.parametrize("kind,status,code", [
    ("PreparationError", 422, "invalid_file"), ("RuntimeError", 500, "internal_error"),
])
def test_legacy_pipeline_errors_have_safe_fallback(kind, status, code):
    actual_status, payload = pipeline_error(PipelineError("prepare", kind, "secret"), "request-123")
    assert (actual_status, payload["error"]["code"]) == (status, code)
    assert "secret" not in json.dumps(payload)


def test_unknown_code_and_user_controlled_validation_details_are_not_exposed():
    error = APIError("secret/code", {"fields": [
        {"location": ["query", "workflow"], "type": "literal_error", "input": "secret"},
        {"location": ["query", "secret"], "type": "secret error"},
    ], "message": "secret"})
    payload = json.loads(error_response(error.code, "request-123", error.details).body)
    assert payload["error"]["code"] == "internal_error"
    assert payload["error"]["details"] == {"fields": [
        {"location": ["query", "workflow"], "type": "literal_error"},
    ]}
    assert "secret" not in json.dumps(payload)


def test_pipeline_carries_typed_error_to_http_mapping():
    class TimeoutStep:
        input_type = str
        output_type = str
        version = "1"
        config = {}

        def run(self, value):
            raise ProviderTimeoutError("private provider information")

    result = Pipeline([("analyze", TimeoutStep())]).run("input")
    assert not result.ok and result.error.code == "provider_timeout"
    status, payload = pipeline_error(result.error, "request-123")
    assert status == 504
    assert "private" not in json.dumps(payload)
