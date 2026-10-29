import base64
from io import BytesIO
import json
import random

import pytest
from PIL import Image

from extraction.analysis import ReceiptImageAnalyzer, ReceiptItemClassifier
from extraction.analysis.receipt_images import GEMINI_MODEL
from extraction.contracts import PreparationConfig, SourceFile, SourceFiles
from extraction.providers import OpenRouterClient
from extraction.reporting import to_dict
from extraction.steps.images import PreparedImagesStep
from extraction.steps.ocr import RapidOCRStep
from extraction.steps.ocr.common import fragment
from extraction.workflows import service_pipelines, warmup_service
from test_gemma_vision import response as vision_answer


def sources():
    files = []
    for index, size in enumerate(((20, 10), (30, 12))):
        buffer = BytesIO()
        image = Image.new("RGB", size, "white")
        if index == 0:
            exif = image.getexif()
            exif[274] = 6
            image.save(buffer, format="JPEG", exif=exif)
        else:
            image.save(buffer, format="PNG")
        files.append(SourceFile(buffer.getvalue(), "image/jpeg" if index == 0 else "image/png", f"input-{index}"))
    return SourceFiles(files)


def workflows(requests):
    def policy(body, send):
        requests.append(json.loads(body))
        return {"choices": [{"finish_reason": "stop", "message": {"content": json.dumps(vision_answer())}}]}
    client = OpenRouterClient("test_key")
    analyzer = ReceiptImageAnalyzer(client, allow_paid=True, model=GEMINI_MODEL, request_policy=policy)
    classifier = ReceiptItemClassifier(client, allow_paid=True, request_policy=policy)
    return service_pipelines(classifier, analyzer)


def test_service_composition_and_shared_rapidocr():
    pipelines = workflows([])
    assert list(dict(pipelines["vision_ocr"].steps)) == ["prepare", "orient", "ocr", "images", "analyze", "validate"]
    assert list(dict(pipelines["vision"].steps)) == ["prepare", "orient", "images", "analyze", "validate"]
    assert list(dict(pipelines["text"].steps)) == ["prepare", "orient", "ocr", "parse", "classify", "validate"]
    for name in ("prepare", "orient"):
        assert len({id(dict(p.steps)[name]) for p in pipelines.values()}) == 1
    engine = dict(pipelines["vision_ocr"].steps)["ocr"]
    assert isinstance(engine, RapidOCRStep)
    assert engine is dict(pipelines["text"].steps)["ocr"]
    assert engine.config["detector"] == "PP-OCRv5-mobile"
    assert engine.config["recognizer"] == "PP-OCRv5-mobile-latin"
    assert engine.config["device"] == "cpu"


def test_warmup_exercises_shared_models_without_provider(monkeypatch):
    requests = []
    pipelines = workflows(requests)
    steps = dict(pipelines["vision_ocr"].steps)
    calls = []
    monkeypatch.setattr(steps["orient"], "_predict", lambda image: (calls.append("orient") or (0, 0.99)))
    monkeypatch.setattr(steps["ocr"], "initialize", lambda: calls.append("initialize"))
    monkeypatch.setattr(steps["ocr"], "recognize", lambda content: calls.append("recognize") or [])
    warmup_service(pipelines)
    assert calls == ["orient", "initialize", "recognize"]
    assert requests == []


def test_warmup_propagates_model_failure(monkeypatch):
    pipelines = workflows([])
    def fail(image):
        raise RuntimeError("Model unavailable")
    monkeypatch.setattr(dict(pipelines["vision_ocr"].steps)["orient"], "_predict", fail)
    import pytest
    with pytest.raises(RuntimeError, match="warmup failed at orient"):
        warmup_service(pipelines)


def test_warmup_does_not_apply_upload_pixel_limits(monkeypatch):
    pipelines = workflows([])
    steps = dict(pipelines["vision_ocr"].steps)
    steps["prepare"].settings = PreparationConfig(max_page_pixels=1)
    monkeypatch.setattr(steps["orient"], "_predict", lambda image: (0, 0.99))
    monkeypatch.setattr(steps["ocr"], "initialize", lambda: None)
    monkeypatch.setattr(steps["ocr"], "recognize", lambda content: [])
    warmup_service(pipelines)
    assert steps["prepare"].settings.max_page_pixels == 1


def test_hybrid_sends_rotated_images_and_matching_raw_ocr(monkeypatch):
    requests = []
    pipeline = workflows(requests)["vision_ocr"]
    steps = dict(pipeline.steps)
    monkeypatch.setattr(steps["orient"], "_predict", lambda image: (90, 0.99))
    monkeypatch.setattr(steps["ocr"], "initialize", lambda: None)

    def recognize(content):
        with Image.open(BytesIO(content)) as image:
            w, h = image.size
        return [fragment("RAW OCR", 0.99, ((0, 0), (w, 0), (w, h), (0, h)))]

    monkeypatch.setattr(steps["ocr"], "recognize", recognize)
    result = pipeline.run(sources(), capture_steps=("orient", "ocr", "images"))
    assert result.ok
    document = result.captured_steps["orient"]
    assert [(p.width, p.height) for p in document.pages] == [(20, 10), (12, 30)]
    combined = result.captured_steps["images"]
    assert combined.ocr is result.captured_steps["ocr"]
    assert [s.content for s in combined.sources.files] == [p.content for p in document.pages]
    assert len(requests) == 1
    content = requests[0]["messages"][1]["content"]
    sent_images = [part["image_url"]["url"] for part in content if part["type"] == "image_url"]
    assert [base64.b64decode(url.split(",", 1)[1]) for url in sent_images] == [p.content for p in document.pages]
    assert "RAW OCR" in content[-1]["text"]
    guidance = steps["analyze"].analyzer.ocr_guidance
    raw_ocr = json.loads(content[-1]["text"][len(guidance):])
    assert [p["page"] for p in raw_ocr] == [1, 2]
    assert all(set(p) == {"page", "fragments"} for p in raw_ocr)
    assert raw_ocr[0]["fragments"][0]["box"] == [[0, 0], [1000, 0], [1000, 1000], [0, 1000]]
    serialized = json.dumps(to_dict(result))
    assert "base64" not in serialized and "test_key" not in serialized
    assert all("content" not in s for s in to_dict(combined)["sources"]["files"])


def test_vision_uses_prepared_rotated_pages_without_ocr(monkeypatch):
    requests = []
    pipeline = workflows(requests)["vision"]
    steps = dict(pipeline.steps)
    assert isinstance(steps["images"], PreparedImagesStep)
    monkeypatch.setattr(steps["orient"], "_predict", lambda image: (90, 0.99))
    result = pipeline.run(sources(), capture_steps=("orient",))
    assert result.ok and len(requests) == 1
    content = requests[0]["messages"][1]["content"]
    sent = [base64.b64decode(p["image_url"]["url"].split(",", 1)[1])
            for p in content if p["type"] == "image_url"]
    assert sent == [p.content for p in result.captured_steps["orient"].pages]


@pytest.mark.parametrize("workflow", ["vision_ocr", "vision"])
def test_service_optimizes_photo_transport_without_resizing_or_changing_ocr(monkeypatch, workflow):
    buffer = BytesIO()
    size = (256, 192)
    Image.frombytes("RGB", size, random.Random(42).randbytes(size[0] * size[1] * 3)).save(buffer, format="PNG")
    original = buffer.getvalue()
    requests = []
    pipeline = workflows(requests)[workflow]
    steps = dict(pipeline.steps)
    monkeypatch.setattr(steps["orient"], "_predict", lambda image: (90, 0.99))
    ocr_inputs = []
    if workflow == "vision_ocr":
        monkeypatch.setattr(steps["ocr"], "initialize", lambda: None)
        monkeypatch.setattr(steps["ocr"], "recognize", lambda content: ocr_inputs.append(content) or [])
    result = pipeline.run(SourceFiles((SourceFile(original, "image/png", "photo.png"),)),
                          capture_steps=("orient", "ocr", "images"))
    assert result.ok and len(requests) == 1
    assert requests[0]["model"] == GEMINI_MODEL
    document = result.captured_steps["orient"]
    combined = result.captured_steps["images"]
    if workflow == "vision_ocr":
        assert ocr_inputs == [document.pages[0].content]
        assert combined.ocr is result.captured_steps["ocr"]
        assert combined.ocr.document is document
        transported = combined.sources.files[0]
    else:
        transported = combined.files[0]
    assert transported.mime_type == "image/jpeg" and transported.name == "page-1.jpg"
    assert len(transported.content) < len(document.pages[0].content)
    with Image.open(BytesIO(transported.content)) as image:
        assert image.format == "JPEG" and image.size == (192, 256)
    assert document.pages[0].mime_type == "image/png"
    assert document.sources[0].content == original
    parts = requests[0]["messages"][1]["content"]
    url = next(p["image_url"]["url"] for p in parts if p["type"] == "image_url")
    assert url.startswith("data:image/jpeg;base64,")
    assert base64.b64decode(url.split(",", 1)[1]) == transported.content
    assert steps["images"].config["resize"] is False
