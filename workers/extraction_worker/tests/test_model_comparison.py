import copy
from decimal import Decimal

import pytest

from experiments.model_comparison import MODELS, model_request, reservation
from extraction.contracts import OCRResult, PreparedDocument, OCRPage
from test_gemma_vision import source


def data():
    return [source()], OCRResult(PreparedDocument((), ()),
        (OCRPage(1, "private", 1, 1, 32, 40, ()),), "test", "1", {})


def test_models_receive_identical_images_ocr_prompt_and_output_limit():
    images, ocr = data()
    baseline = model_request(images, ocr, "gemma31")
    for variant, model in MODELS.items():
        request = model_request(images, ocr, variant)
        assert request["model"] == model
        assert request["messages"] == baseline["messages"]
        assert request["max_tokens"] == 8192 and request["temperature"] == 0
        assert request["stream"] and request["service_tier"] == "default"
        assert request["reasoning"] == ({"effort": "minimal"} if variant == "gemini_lite" else {"enabled": False})


def test_reservation_covers_full_context_output_and_possible_image_fees():
    images, ocr = data()
    request = model_request(images, ocr, "gemini_lite")
    catalog = {"models": {request["model"]: {"context_length": 1048576,
                "architecture": {"input_modalities": ["text", "image"]}}}}
    assert reservation(request, catalog, 1) == Decimal("0.33768")
    assert reservation(request, catalog, 4) == Decimal("0.36768")
    no_vision = copy.deepcopy(catalog)
    no_vision["models"][request["model"]]["architecture"]["input_modalities"] = ["text"]
    with pytest.raises(ValueError, match="images"):
        reservation(request, no_vision, 1)


def test_unapproved_model_rejected():
    with pytest.raises(ValueError):
        model_request(*data(), "unknown")
