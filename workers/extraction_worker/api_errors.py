"""Public error codes and safe HTTP responses for the extraction service."""

from fastapi.responses import JSONResponse

from api_models import ErrorBody, ErrorResponse


ERRORS = {
    "invalid_request": (400, "Invalid request."),
    "not_found": (404, "Resource not found."),
    "method_not_allowed": (405, "Method not allowed."),
    "request_too_large": (413, "Request exceeds the upload limit."),
    "file_too_large": (413, "Uploaded file exceeds the size limit."),
    "image_too_large": (413, "Document exceeds the pixel limit."),
    "unsupported_format": (415, "Unsupported document format."),
    "invalid_file": (422, "Cannot read the uploaded document."),
    "empty_file": (422, "Uploaded files must not be empty."),
    "password_required": (422, "Password-protected documents are not supported."),
    "too_many_pages": (422, "Choose a document with at most four pages in total."),
    "invalid_file_count": (422, "Choose one to four files of one receipt."),
    "validation_error": (422, "Request validation failed."),
    "provider_error": (502, "The extraction provider returned an error."),
    "provider_network_error": (502, "Cannot connect to the extraction provider."),
    "provider_response_invalid": (502, "The extraction provider returned an invalid response."),
    "provider_unavailable": (503, "The extraction provider is temporarily unavailable."),
    "provider_timeout": (504, "The extraction provider did not respond in time."),
    "internal_error": (500, "Internal server error."),
}
STEPS = frozenset(("prepare", "orient", "ocr", "images", "parse", "classify", "analyze", "validate"))
NUMBER_DETAILS = frozenset(("limit_bytes", "file_index", "max_files", "max_pages", "page_count",
                            "max_pixels", "pixels", "file_count"))


def safe_details(details):
    if not isinstance(details, dict):
        return None
    safe = {key: value for key, value in details.items()
            if key in NUMBER_DETAILS and type(value) is int and value >= 0}
    if isinstance(details.get("step"), str) and details["step"] in STEPS:
        safe["step"] = details["step"]
    if isinstance(details.get("fields"), list):
        fields = []
        for entry in details["fields"]:
            if not isinstance(entry, dict):
                continue
            location = entry.get("location")
            kind = entry.get("type")
            if (isinstance(location, list) and all(type(part) is int or
                    isinstance(part, str) and part in {"body", "query", "path", "files", "workflow"}
                    for part in location) and isinstance(kind, str)
                    and kind.replace("_", "").replace(".", "").isalnum()):
                fields.append({"location": location, "type": kind})
        safe["fields"] = fields
    return safe or None


class APIError(Exception):
    def __init__(self, code, details=None):
        self.code = code if isinstance(code, str) and code in ERRORS else "internal_error"
        self.details = safe_details(details)
        super().__init__(ERRORS[self.code][1])


def error_payload(code, request_id, details=None):
    code = code if isinstance(code, str) and code in ERRORS else "internal_error"
    status, message = ERRORS[code]
    payload = ErrorResponse(error=ErrorBody(
        code=code, message=message, request_id=request_id, details=safe_details(details),
    )).model_dump(mode="json")
    return status, payload


def error_response(code, request_id, details=None):
    status, payload = error_payload(code, request_id, details)
    return JSONResponse(status_code=status, content=payload, headers={"X-Request-ID": request_id})


def pipeline_error(error, request_id):
    code = getattr(error, "code", None)
    if not isinstance(code, str) or code not in ERRORS:
        code = "invalid_file" if error.exception_type == "PreparationError" else "internal_error"
    details = safe_details(getattr(error, "details", None)) or {}
    if error.step in STEPS:
        details["step"] = error.step
    return error_payload(code, request_id, details)
