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

from extraction.analysis import ReceiptItemClassifier
from extraction.contracts import SourceFile, SourceFiles
from extraction.providers import OpenRouterClient
from extraction.reporting import to_dict
from main import build_pipeline

logger = logging.getLogger("uvicorn.error")

# Swagger UI recognizes format=binary as a file picker. This only overrides
# JSON Schema; FastAPI still validates and receives ordinary UploadFile objects.
DocumentUpload = Annotated[UploadFile, WithJsonSchema({"type": "string", "format": "binary"})]


@asynccontextmanager
async def lifespan(app):
    client = OpenRouterClient(os.environ.get("OPENROUTER_API_KEY", ""))
    classifier = ReceiptItemClassifier(client, allow_paid=True, cache_dir=None)
    app.state.pipeline = build_pipeline(classifier=classifier)
    app.state.pipeline_lock = Lock()
    yield


app = FastAPI(title="Receipt extraction", lifespan=lifespan)


@app.exception_handler(Exception)
async def unexpected_error(request: Request, exc: Exception):
    logger.error("Unexpected API failure", exc_info=(type(exc), exc, exc.__traceback__))
    return JSONResponse(status_code=500, content={"error": {"message": "Internal server error"}})


@app.get("/health")
async def health():
    return {"status": "ok"}


def execute_pipeline(app, sources, request_id):
    # Keep the lock inside the synchronous call: cancelling its awaiter must
    # never allow a second OCR run while this thread is still working.
    started = perf_counter()
    with app.state.pipeline_lock:
        result = app.state.pipeline.run(sources)
        status = 200
        if not result.ok:
            invalid_input = (result.error.step == "prepare"
                             and result.error.exception_type == "PreparationError")
            status = 422 if invalid_input else 500
        payload = to_dict(result) if result.ok else {"error": to_dict(result.error)}
    logger.info("Extraction request_id=%s status=%s step=%s seconds=%.3f",
                request_id, status, result.error.step if result.error else "complete",
                perf_counter() - started)
    return status, payload


@app.post("/api/v1/extract")
async def extract(request: Request, files: Annotated[list[DocumentUpload], File()]):
    try:
        if not files:
            raise HTTPException(status_code=422, detail="At least one file is required")
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
            execute_pipeline, request.app, SourceFiles(sources), uuid4().hex,
        )
        return JSONResponse(status_code=status, content=payload)
    finally:
        for file in files:
            await file.close()
