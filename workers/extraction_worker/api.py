"""Stateless HTTP adapter for the existing classified receipt pipeline."""

from contextlib import asynccontextmanager
import logging
import os
from threading import Lock
from time import perf_counter
from typing import Annotated
from uuid import uuid4

from fastapi import FastAPI, File, Request, UploadFile
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import WithJsonSchema
from starlette.concurrency import run_in_threadpool
from starlette.exceptions import HTTPException

from api_errors import APIError, error_response, pipeline_error
from api_limits import RequestLimitsMiddleware, UploadLimits, UploadLimitError, read_upload
from api_models import ErrorResponse, ExtractionResponse, application_response
from extraction.analysis import ReceiptImageAnalyzer, ReceiptItemClassifier
from extraction.analysis.receipt_images import GEMINI_MODEL
from extraction.contracts import PreparationConfig, SourceFile, SourceFiles
from extraction.providers import OpenRouterClient
from extraction.reporting import to_dict
from extraction.workflows import WORKFLOW_NAMES, Workflow, service_pipelines, warmup_service

logger = logging.getLogger("uvicorn.error")

# Swagger UI recognizes format=binary as a file picker. This only overrides
# JSON Schema; FastAPI still validates and receives ordinary UploadFile objects.
DocumentUpload = Annotated[UploadFile, WithJsonSchema({"type": "string", "format": "binary"})]


@asynccontextmanager
async def lifespan(app):
    limits = UploadLimits.from_env()
    workflow = os.environ.get("EXTRACTION_WORKFLOW", "vision_ocr")
    if workflow not in WORKFLOW_NAMES:
        raise ValueError(f"EXTRACTION_WORKFLOW must be one of {WORKFLOW_NAMES}")
    client = OpenRouterClient(os.environ.get("OPENROUTER_API_KEY", ""))
    classifier = ReceiptItemClassifier(client, allow_paid=True, cache_dir=None)
    analyzer = ReceiptImageAnalyzer(client, allow_paid=True, cache_dir=None,
                                    model=GEMINI_MODEL,
                                    routing=os.environ.get("EXTRACTION_VISION_ROUTING", "throughput"))
    app.state.upload_limits = limits
    app.state.pipelines = service_pipelines(classifier, analyzer, preparation_config=PreparationConfig(
        max_pages=4, max_page_pixels=limits.max_page_pixels, max_document_pixels=limits.max_document_pixels,
    ))
    app.state.workflow = workflow
    app.state.pipeline_lock = Lock()
    logger.info("Warming up orientation and RapidOCR models before accepting requests")
    started = perf_counter()
    await run_in_threadpool(warmup_service, app.state.pipelines)
    logger.info("Local models ready; warmup_seconds=%.3f", perf_counter() - started)
    yield


app = FastAPI(title="Receipt extraction", lifespan=lifespan)


class RequestContextMiddleware:
    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            return await self.app(scope, receive, send)
        request_id = uuid4().hex
        scope.setdefault("state", {})["request_id"] = request_id
        response_started = False

        async def send_with_id(message):
            nonlocal response_started
            if message["type"] == "http.response.start":
                response_started = True
                headers = [(key, value) for key, value in message.get("headers", []) if key.lower() != b"x-request-id"]
                message = {**message, "headers": [*headers, (b"x-request-id", request_id.encode("ascii"))]}
            await send(message)

        try:
            await self.app(scope, receive, send_with_id)
        except Exception as exc:
            if response_started:
                raise RuntimeError("Response stream failed") from None
            response = await unexpected_error(Request(scope), exc)
            await response(scope, receive, send_with_id)


app.add_middleware(RequestLimitsMiddleware, error_response=error_response)
app.add_middleware(RequestContextMiddleware)


@app.exception_handler(APIError)
@app.exception_handler(UploadLimitError)
async def input_error(request: Request, exc):
    return error_response(exc.code, request.state.request_id, getattr(exc, "details", None))


@app.exception_handler(RequestValidationError)
async def validation_error(request: Request, exc: RequestValidationError):
    fields = [{"location": list(error["loc"]), "type": error["type"]} for error in exc.errors()]
    return error_response("validation_error", request.state.request_id, {"fields": fields})


@app.exception_handler(HTTPException)
async def http_error(request: Request, exc: HTTPException):
    code = {400: "invalid_request", 404: "not_found", 405: "method_not_allowed",
            413: "request_too_large", 415: "unsupported_format", 422: "validation_error"}.get(exc.status_code, "internal_error")
    response = error_response(code, request.state.request_id)
    if exc.status_code == 405:
        response.headers.update(exc.headers or {})
    return response


@app.exception_handler(Exception)
async def unexpected_error(request: Request, exc: Exception):
    logger.error("Unexpected API failure request_id=%s exception_type=%s", request.state.request_id, type(exc).__name__)
    return error_response("internal_error", request.state.request_id)


@app.get("/health")
async def health():
    return {"status": "ok"}


def execute_pipeline(app, sources, request_id, workflow, debug=False):
    # Keep the lock inside the synchronous call: cancelling its awaiter must
    # never allow a second OCR run while this thread is still working.
    started = perf_counter()
    with app.state.pipeline_lock:
        acquired = perf_counter()
        result = app.state.pipelines[workflow].run(sources, capture_steps=("ocr",) if debug else False)
        finished = perf_counter()
        status = 200
        if not result.ok:
            status, payload = pipeline_error(result.error, request_id)
            logger.error("Pipeline failure request_id=%s step=%s exception_type=%s code=%s",
                         request_id, result.error.step, result.error.exception_type, payload["error"]["code"])
        else:
            payload = to_dict(result) if debug else application_response(result).model_dump(mode="json")
        if debug:
            if not result.ok:
                payload.update(steps=to_dict(result.steps), warnings=to_dict(result.warnings))
            payload.update(workflow=workflow, ocr=to_dict(result.captured_steps.get("ocr")),
                           timing={"wait_seconds": acquired - started,
                                   "pipeline_seconds": finished - acquired})
    logger.info("Extraction request_id=%s workflow=%s status=%s step=%s wait_seconds=%.3f pipeline_seconds=%.3f",
                request_id, workflow, status, result.error.step if result.error else "complete",
                acquired - started, finished - acquired)
    return status, payload


async def handle_extract(request, files, workflow, debug=False):
    try:
        if not 1 <= len(files) <= 4:
            raise APIError("invalid_file_count", {"max_files": 4})
        sources = []
        for index, file in enumerate(files, 1):
            content = await read_upload(file, request.app.state.upload_limits, index)
            if not content:
                raise APIError("empty_file", {"file_index": index})
            sources.append(SourceFile(content=content, mime_type=file.content_type or "application/octet-stream",
                                      name=file.filename or f"image-{index}"))
        status, payload = await run_in_threadpool(
            execute_pipeline, request.app, SourceFiles(sources), request.state.request_id, workflow, debug,
        )
        return JSONResponse(status_code=status, content=payload)
    finally:
        for file in files:
            await file.close()


ERROR_RESPONSES = {status: {"model": ErrorResponse} for status in (400, 413, 415, 422, 500, 502, 503, 504)}


@app.post("/api/v1/extract", response_model=ExtractionResponse, responses=ERROR_RESPONSES)
async def extract(request: Request, files: Annotated[list[DocumentUpload], File()]):
    return await handle_extract(request, files, request.app.state.workflow)


@app.post("/api/v1/debug/extract", responses=ERROR_RESPONSES)
async def debug_extract(request: Request, files: Annotated[list[DocumentUpload], File()],
                        workflow: Workflow | None = None):
    return await handle_extract(request, files, workflow or request.app.state.workflow, debug=True)
