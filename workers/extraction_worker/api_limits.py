"""Bounded HTTP ingestion before multipart parsing and per-file reads."""

from dataclasses import dataclass
import os
import re
from tempfile import SpooledTemporaryFile
from uuid import uuid4

from starlette.responses import JSONResponse


CHUNK_BYTES = 64 * 1024
SPOOL_MEMORY_BYTES = 1024 * 1024
EXTRACTION_PATHS = frozenset(("/api/v1/extract", "/api/v1/debug/extract"))


@dataclass(frozen=True)
class UploadLimits:
    max_file_bytes: int = 20 * 1024 * 1024
    max_request_bytes: int = 50 * 1024 * 1024
    max_page_pixels: int = 50_000_000
    max_document_pixels: int = 100_000_000

    def __post_init__(self):
        for name in self.__dataclass_fields__:
            value = getattr(self, name)
            if type(value) is not int or value <= 0:
                raise ValueError(f"{name} must be a positive integer")
        if self.max_file_bytes > self.max_request_bytes:
            raise ValueError("max_file_bytes must not exceed max_request_bytes")
        if self.max_page_pixels > self.max_document_pixels:
            raise ValueError("max_page_pixels must not exceed max_document_pixels")

    @classmethod
    def from_env(cls, environ=None):
        environ = os.environ if environ is None else environ
        values = {}
        for name in cls.__dataclass_fields__:
            variable = f"EXTRACTION_{name.upper()}"
            if variable in environ:
                value = environ[variable].strip()
                if not re.fullmatch(r"[0-9]+", value) or int(value) <= 0:
                    raise ValueError(f"{variable} must be a positive integer")
                values[name] = int(value)
        return cls(**values)


class UploadLimitError(ValueError):
    code = "file_too_large"
    status_code = 413

    def __init__(self, file_index, limit_bytes):
        super().__init__("Uploaded file exceeds the size limit.")
        self.details = {"file_index": file_index, "limit_bytes": limit_bytes}


async def read_upload(file, limits, index):
    """Read one UploadFile within its byte limit; the caller owns file cleanup."""
    content = bytearray()
    while chunk := await file.read(CHUNK_BYTES):
        if len(content) + len(chunk) > limits.max_file_bytes:
            raise UploadLimitError(index, limits.max_file_bytes)
        content.extend(chunk)
    return bytes(content)


def _limit_response(code, request_id, details):
    return JSONResponse(status_code=413, content={"error": {
        "code": code,
        "message": "Request exceeds the upload limit.",
        "request_id": request_id,
        "details": details,
    }})


class RequestLimitsMiddleware:
    """Check the complete body before FastAPI can spool multipart file parts.

    The optional response factory receives (code, request_id, details). The API
    can supply its common error renderer; standalone use has the same envelope.
    """

    def __init__(self, app, *, limits=None, error_response=None):
        self.app = app
        self.limits = limits
        self.error_response = error_response or _limit_response

    def _limits(self, scope):
        if self.limits is not None:
            return self.limits
        app = scope.get("app")
        return getattr(getattr(app, "state", None), "upload_limits", None) or UploadLimits()

    async def __call__(self, scope, receive, send):
        if (scope["type"] != "http" or scope.get("method") != "POST"
                or scope.get("path") not in EXTRACTION_PATHS):
            await self.app(scope, receive, send)
            return
        limits = self._limits(scope)
        request_id = scope.setdefault("state", {}).setdefault("request_id", uuid4().hex)

        async def reject():
            response = self.error_response("request_too_large", request_id,
                                           {"limit_bytes": limits.max_request_bytes})
            await response(scope, receive, send)

        for name, value in scope.get("headers", ()):
            if name.lower() != b"content-length" or not re.fullmatch(rb"[0-9]+", value):
                continue
            normalized = value.lstrip(b"0") or b"0"
            ceiling = str(limits.max_request_bytes).encode("ascii")
            if (len(normalized) > len(ceiling)
                    or len(normalized) == len(ceiling) and normalized > ceiling):
                await reject()
                return

        with SpooledTemporaryFile(max_size=SPOOL_MEMORY_BYTES, mode="w+b") as spool:
            total = 0
            while True:
                message = await receive()
                if message["type"] == "http.disconnect":
                    return
                if message["type"] != "http.request":
                    raise RuntimeError("Unexpected ASGI request message")
                body = message.get("body", b"")
                total += len(body)
                if total > limits.max_request_bytes:
                    await reject()
                    return
                if spool.tell() + len(body) > SPOOL_MEMORY_BYTES:
                    spool.rollover()
                spool.write(body)
                if not message.get("more_body", False):
                    break
            spool.seek(0)
            replayed = False

            async def replay():
                nonlocal replayed
                if replayed:
                    return await receive()
                body = spool.read(CHUNK_BYTES)
                more_body = spool.tell() < total
                replayed = not more_body
                return {"type": "http.request", "body": body, "more_body": more_body}

            await self.app(scope, replay, send)
