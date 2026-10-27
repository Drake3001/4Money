"""Stateless HTTP adapter for the existing classified receipt pipeline."""

from contextlib import asynccontextmanager
import logging
import os
from threading import Lock
from time import perf_counter
from typing import Annotated
from uuid import uuid4

from fastapi import FastAPI, File, HTTPException, Request, UploadFile
from fastapi.responses import JSONResponse
from pydantic import WithJsonSchema
from starlette.concurrency import run_in_threadpool

from api_models import ExtractionResponse, application_response
from extraction.analysis import ReceiptImageAnalyzer, ReceiptItemClassifier
from extraction.analysis.receipt_images import GEMINI_MODEL
from extraction.contracts import SourceFile, SourceFiles
from extraction.providers import OpenRouterClient
from extraction.reporting import to_dict
from extraction.workflows import WORKFLOW_NAMES, Workflow, service_pipelines, warmup_service

logger = logging.getLogger("uvicorn.error")

# Swagger UI recognizes format=binary as a file picker. This only overrides
# JSON Schema; FastAPI still validates and receives ordinary UploadFile objects.
DocumentUpload = Annotated[UploadFile, WithJsonSchema({"type": "string", "format": "binary"})]


@asynccontextmanager
async def lifespan(app):
    workflow = os.environ.get("EXTRACTION_WORKFLOW", "vision_ocr")
    if workflow not in WORKFLOW_NAMES:
        raise ValueError(f"EXTRACTION_WORKFLOW must be one of {WORKFLOW_NAMES}")
    client = OpenRouterClient(os.environ.get("OPENROUTER_API_KEY", ""))
    classifier = ReceiptItemClassifier(client, allow_paid=True, cache_dir=None)
    analyzer = ReceiptImageAnalyzer(client, allow_paid=True, cache_dir=None,
                                    model=GEMINI_MODEL,
                                    routing=os.environ.get("EXTRACTION_VISION_ROUTING", "throughput"))
    app.state.pipelines = service_pipelines(classifier, analyzer)
    app.state.workflow = workflow
    app.state.pipeline_lock = Lock()
    logger.info("Warming up orientation and RapidOCR models before accepting requests")
    started = perf_counter()
    await run_in_threadpool(warmup_service, app.state.pipelines)
    logger.info("Local models ready; warmup_seconds=%.3f", perf_counter() - started)
    yield


app = FastAPI(title="Receipt extraction", lifespan=lifespan)


@app.exception_handler(Exception)
async def unexpected_error(request: Request, exc: Exception):
    logger.error("Unexpected API failure", exc_info=(type(exc), exc, exc.__traceback__))
    return JSONResponse(status_code=500, content={"error": {"message": "Internal server error"}})


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
            invalid_input = (result.error.step == "prepare"
                             and result.error.exception_type == "PreparationError")
            status = 422 if invalid_input else 500
        if debug:
            payload = to_dict(result) if result.ok else {
                "error": to_dict(result.error), "steps": to_dict(result.steps),
                "warnings": to_dict(result.warnings),
            }
            payload.update(workflow=workflow, ocr=to_dict(result.captured_steps.get("ocr")),
                           timing={"wait_seconds": acquired - started,
                                   "pipeline_seconds": finished - acquired})
        else:
            payload = (application_response(result).model_dump(mode="json") if result.ok
                       else {"error": to_dict(result.error)})
    logger.info("Extraction request_id=%s workflow=%s status=%s step=%s wait_seconds=%.3f pipeline_seconds=%.3f",
                request_id, workflow, status, result.error.step if result.error else "complete",
                acquired - started, finished - acquired)
    return status, payload


async def handle_extract(request, files, workflow, debug=False):
    try:
        if not 1 <= len(files) <= 4:
            raise HTTPException(status_code=422, detail="Choose 1–4 images of one receipt")
        sources = []
        for index, file in enumerate(files, 1):
            if file.content_type not in {"image/jpeg", "image/png"}:
                raise HTTPException(status_code=415, detail="Only JPEG and PNG are supported")
            content = await file.read()
            if not content:
                raise HTTPException(status_code=422, detail="Files must not be empty")
            sources.append(SourceFile(content=content, mime_type=file.content_type,
                                      name=file.filename or f"image-{index}"))
        status, payload = await run_in_threadpool(
            execute_pipeline, request.app, SourceFiles(sources), uuid4().hex, workflow, debug,
        )
        return JSONResponse(status_code=status, content=payload)
    finally:
        for file in files:
            await file.close()


@app.post("/api/v1/extract", response_model=ExtractionResponse)
async def extract(request: Request, files: Annotated[list[DocumentUpload], File()]):
    return await handle_extract(request, files, request.app.state.workflow)


@app.post("/api/v1/debug/extract")
async def debug_extract(request: Request, files: Annotated[list[DocumentUpload], File()],
                        workflow: Workflow | None = None):
    return await handle_extract(request, files, workflow or request.app.state.workflow, debug=True)
