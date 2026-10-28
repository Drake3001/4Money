"""Local files and batch execution shared by main and experiments."""

import tempfile
from datetime import datetime
from pathlib import Path

from .contracts import ExtractionWarning, OCRResult, PipelineError, PipelineResult, SourceFile, SourceFiles
from .reporting import write_environment, write_result, write_summary
from .formats import MIME_TYPES


def collect_jobs(folder):
    jobs = []
    for path in sorted(Path(folder).iterdir(), key=lambda p: p.name):
        if path.is_file() and path.suffix.lower() in MIME_TYPES:
            jobs.append((path.name, (path,)))
        elif path.is_dir():
            files = tuple(
                sorted(
                    (
                        p
                        for p in path.iterdir()
                        if p.is_file() and p.suffix.lower() in MIME_TYPES
                    ),
                    key=lambda p: p.name,
                )
            )
            if files:
                jobs.append((path.name, files))
    return jobs


def read_sources(files):
    return SourceFiles(
        tuple(
            SourceFile(path.read_bytes(), MIME_TYPES[path.suffix.lower()], path.name)
            for path in files
        )
    )


def new_run(output):
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    return Path(
        tempfile.mkdtemp(prefix=datetime.now().strftime("%Y-%m-%d_%H%M%S_"), dir=output)
    )


def run_case(pipeline, input, folder, debug=False):
    capture = True if debug else {
        name for name, step in pipeline.steps if issubclass(step.output_type, OCRResult)
    }
    result = pipeline.run(input, capture_steps=capture)
    if isinstance(result.output, OCRResult):
        result.warnings.append(ExtractionWarning(
            "ocr_only", "Only OCR was performed; receipt fields and products have not been parsed"
        ))
    write_result(folder, result, save_images=debug)
    return result


def run_batch(pipeline, input_dir, output_dir, debug=False):
    root = new_run(output_dir)
    results = {}
    for name, files in collect_jobs(input_dir):
        try:
            input = read_sources(files)
        except OSError as exc:
            result = PipelineResult(
                error=PipelineError("input", type(exc).__name__, str(exc))
            )
            write_result(root / name, result)
        else:
            result = run_case(pipeline, input, root / name, debug)
        results[name] = result
        print(f"{name}: {'ok' if result.ok else 'error'}", flush=True)
    write_summary(root, results)
    write_environment(root)
    return root, results
