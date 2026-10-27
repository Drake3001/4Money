"""Prepare documents once, run one process per OCR, then compare the results."""

import json
import multiprocessing
import os
from pathlib import Path
from statistics import median
from time import perf_counter

from extraction.contracts import PipelineError, PipelineResult, PreparedDocument
from extraction.pipeline import Pipeline
from extraction.reporting import to_dict, write_environment, write_json, write_result, write_summary
from extraction.runner import collect_jobs, new_run, read_sources
from extraction.steps.prepare import PrepareDocumentStep

from .ocr_reporting import write_comparison

DEFAULT_VARIANTS = ("rapidocr", "paddle_mobile", "paddle_server_det", "easyocr", "tesseract")


def build_variants(names=DEFAULT_VARIANTS, tesseract_command="tesseract", tessdata_dir=None):
    from extraction.steps.ocr import EasyOCRStep, PaddleOCRStep, RapidOCRStep, TesseractStep

    factories = {
        "rapidocr": RapidOCRStep,
        "paddle_mobile": PaddleOCRStep,
        "paddle_server_det": lambda: PaddleOCRStep("PP-OCRv5_server_det"),
        "easyocr": EasyOCRStep,
        "tesseract": lambda: TesseractStep(tesseract_command, tessdata_dir),
    }
    if not names or len(set(names)) != len(names) or any(name not in factories for name in names):
        raise ValueError("OCR variants must be a nonempty, unique selection of known variants")
    return {name: factories[name]() for name in names}


def run_ocr_comparison(input_dir, output_dir, dpi=200, debug=False, names=DEFAULT_VARIANTS,
                       repeats=1, tesseract_command="tesseract", tessdata_dir=None):
    if isinstance(repeats, bool) or not isinstance(repeats, int) or repeats < 1:
        raise ValueError("OCR repeats must be a positive integer")
    engines = build_variants(names, tesseract_command, tessdata_dir)
    input_dir, root = Path(input_dir), new_run(output_dir)
    prepare = Pipeline([("prepare", PrepareDocumentStep(dpi))])
    prepared = {}
    for name, files in collect_jobs(input_dir):
        try:
            result = prepare.run(read_sources(files), capture_steps=debug)
        except OSError as exc:
            result = PipelineResult(error=PipelineError("input", type(exc).__name__, str(exc)))
        prepared[name] = result
        write_result(root / "preparation" / name, result)

    # Child processes inherit these limits before importing native runtimes.
    os.environ["OMP_NUM_THREADS"] = "4"
    os.environ["OMP_THREAD_LIMIT"] = "4"
    states = {}
    for variant, step in engines.items():
        states[variant] = _isolated_variant(step, prepared, root / variant, repeats)
    write_environment(root)
    write_comparison(root, input_dir, prepared, states, repeats)
    failed = any(not result.ok for result in prepared.values()) or any(
        state["failed"] for state in states.values()
    )
    return root, failed


def _isolated_variant(step, prepared, directory, repeats):
    process = multiprocessing.get_context("spawn").Process(
        target=_run_variant, args=(step, prepared, directory, repeats)
    )
    started = perf_counter()
    process.start()
    try:
        process.join()
    finally:
        if process.is_alive():
            process.terminate()
            process.join()
    if process.exitcode != 0:
        state = {
            "status": "failed", "failed": True, "timings": {},
            "initialization_seconds": None, "warmup_seconds": None,
            "error": to_dict(PipelineError(
                "process", "NativeProcessError", f"OCR process exited with code {process.exitcode}"
            )),
        }
        directory.mkdir(parents=True, exist_ok=True)
        write_json(directory / "process_error.json", state["error"])
    else:
        state = json.loads((directory / "timings.json").read_text(encoding="utf-8"))
    state["process_total_seconds"] = perf_counter() - started
    process.close()
    return state


def _run_variant(step, prepared, directory, repeats):
    """Run a single engine; the process lifetime also releases its native models."""
    directory.mkdir(parents=True, exist_ok=True)
    state = {
        "status": "no_input", "failed": False, "timings": {},
        "initialization_seconds": None, "warmup_seconds": None,
    }
    valid = next((result.output for result in prepared.values() if result.ok), None)
    initialization_error = None
    if valid is not None:
        print(f"{step.name}: initializing", flush=True)
        started = perf_counter()
        try:
            step.initialize()
            state["status"] = "available"
        except Exception as exc:
            initialization_error = PipelineError("initialize", type(exc).__name__, str(exc))
            state.update(status="unavailable", failed=True, error=to_dict(initialization_error))
        state["initialization_seconds"] = perf_counter() - started
        if initialization_error is None:
            print(f"{step.name}: warming up", flush=True)
            started = perf_counter()
            try:
                step.run(PreparedDocument(valid.sources, (valid.pages[0],)))
            except Exception as exc:
                state["warmup_error"] = to_dict(PipelineError("warmup", type(exc).__name__, str(exc)))
                state["failed"] = True
            state["warmup_seconds"] = perf_counter() - started

    pipeline = Pipeline([("ocr", step)])
    results = {}
    for name, preparation in prepared.items():
        runs = []
        if not preparation.ok:
            result = PipelineResult(error=preparation.error)
        elif initialization_error:
            result = PipelineResult(error=initialization_error)
        else:
            for repetition in range(repeats):
                measured = pipeline.run(preparation.output)
                runs.append({"seconds": measured.steps[0].seconds, "ok": measured.ok,
                             "error": to_dict(measured.error)})
                if repetition == 0:
                    result = measured
        state["timings"][name] = {
            "runs": runs,
            "median_seconds": median(run["seconds"] for run in runs)
            if runs and all(run["ok"] for run in runs) else None,
        }
        state["failed"] = state["failed"] or not result.ok or any(not run["ok"] for run in runs)
        results[name] = result
        write_result(directory / name, result)
        print(f"{step.name}/{name}: {'ok' if result.ok else 'error'}", flush=True)
    write_summary(directory, results)
    write_json(directory / "timings.json", state)
