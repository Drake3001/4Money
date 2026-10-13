"""JSON, text and optional page images; no image bytes enter JSON."""

import json
import platform
import sys
from dataclasses import fields, is_dataclass
from datetime import date
from decimal import Decimal
from importlib.metadata import PackageNotFoundError, version

from .contracts import OCRResult, PipelineResult, PreparedDocument, SourceFile


def to_dict(value):
    if isinstance(value, PipelineResult):
        return {
            "output": to_dict(value.output),
            "steps": to_dict(value.steps),
            "warnings": to_dict(value.warnings),
            "error": to_dict(value.error),
        }
    if isinstance(value, SourceFile):
        return {"name": value.name, "mime_type": value.mime_type}
    if isinstance(value, OCRResult):
        return {
            "kind": "ocr",
            "engine": value.engine,
            "version": value.version,
            "settings": to_dict(value.settings),
            "coordinate_system": value.coordinate_system,
            "document": to_dict(value.document),
            "pages": to_dict(value.pages),
        }
    if isinstance(value, (Decimal, date)):
        return str(value) if isinstance(value, Decimal) else value.isoformat()
    if isinstance(value, bytes):
        raise TypeError("Binary data must not be serialized")
    if is_dataclass(value):
        return {
            field.name: to_dict(getattr(value, field.name))
            for field in fields(value)
            if field.name != "content"
        }
    if isinstance(value, (tuple, list)):
        return [to_dict(item) for item in value]
    if isinstance(value, dict):
        return {key: to_dict(item) for key, item in value.items()}
    return value


def result_to_json(result):
    return json.dumps(to_dict(result), ensure_ascii=False, indent=2, allow_nan=False)


def write_result(folder, result):
    folder.mkdir(parents=True, exist_ok=False)
    with (folder / "result.json").open("x", encoding="utf-8") as stream:
        stream.write(result_to_json(result) + "\n")
    if isinstance(result.output, OCRResult):
        text = "\n\n".join(
            f"--- Page {p.page_number}: {p.source_name} ---\n{p.text}"
            for p in result.output.pages
        )
        with (folder / "ocr.txt").open("x", encoding="utf-8") as stream:
            stream.write(text + "\n")
    for name, output in result.captured_steps.items():
        document = output.document if isinstance(output, OCRResult) else output
        if isinstance(document, PreparedDocument):
            directory = folder / "debug" / name
            directory.mkdir(parents=True, exist_ok=False)
            for page in document.pages:
                with (directory / f"{page.number:03d}.png").open("xb") as stream:
                    stream.write(page.content)


def write_summary(folder, results):
    lines = [
        "# Przebieg",
        "",
        "| Paragon | Wynik | Ostrzeżenia | Błąd |",
        "|---|---|---:|---|",
    ]
    for name, result in results.items():
        error = f"{result.error.step}: {result.error.message}" if result.error else ""
        lines.append(
            f"| {name.replace('|', ' ')} | {'ok' if result.ok else 'error'} | {len(result.warnings)} | {error.replace('|', ' ').replace(chr(10), ' ')} |"
        )
    with (folder / "summary.md").open("x", encoding="utf-8") as stream:
        stream.write("\n".join(lines) + "\n")


def write_environment(folder):
    packages = {}
    for name in (
        "Pillow",
        "pypdfium2",
        "rapidocr",
        "onnxruntime",
        "docscan",
        "rembg",
        "paddleocr",
        "paddlex",
        "paddlepaddle",
        "numpy",
        "opencv-python",
        "opencv-python-headless",
        "opencv-contrib-python",
    ):
        try:
            packages[name] = version(name)
        except PackageNotFoundError:
            packages[name] = None
    environment = {
        "python": platform.python_version(),
        "packages": packages,
        "opencv_runtime": getattr(sys.modules.get("cv2"), "__version__", None),
    }
    with (folder / "environment.json").open("x", encoding="utf-8") as stream:
        json.dump(environment, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
