from decimal import Decimal

import pytest

from experiments import model_comparison as study
from test_model_comparison import data


@pytest.fixture
def budget_models(monkeypatch):
    monkeypatch.setattr(study, "MODELS", study.BUDGET_MODELS.copy())


def test_budget_models_use_same_document_prompt_and_standard_tier(budget_models):
    images, ocr = data()
    baseline = study.model_request(images, ocr, "gemini_lite")
    for variant, model in study.MODELS.items():
        payload = study.model_request(images, ocr, variant)
        assert payload["model"] == model
        assert payload["messages"] == baseline["messages"]
        assert payload["max_tokens"] == 8192
        assert payload["service_tier"] == "default"
        assert payload["provider"]["sort"] == "throughput"


def test_unsupported_temperature_is_omitted_and_reasoning_explicit(budget_models):
    luna = study.model_request(*data(), "luna")
    haiku = study.model_request(*data(), "haiku55")
    assert "temperature" not in luna and "temperature" not in haiku
    assert luna["reasoning"] == {"effort": "none"}
    assert haiku["reasoning"] == {"enabled": False, "effort": "low"}
    assert study.model_request(*data(), "gemini25")["reasoning"] == {"enabled": False}
    assert study.model_request(*data(), "qwen37")["reasoning"] == {"enabled": False}


def test_all_budget_model_full_context_bounds_fit_budget(budget_models):
    for variant, model in study.MODELS.items():
        payload = study.model_request(*data(), variant)
        catalog = {"models": {model: {"context_length": 1050000,
                    "architecture": {"input_modalities": ["image", "text"]}}}}
        bound = study.reservation(payload, catalog, 1)
        assert Decimal(0) < bound < Decimal("0.50")
