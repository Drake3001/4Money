import base64
import copy
import json
from io import BytesIO

import pytest
from PIL import Image

from extraction.contracts import SourceFile
from extraction.steps.parsing.vision import (
    GemmaVisionParser, build_vision_request, decode_vision, request_summary, vision_bound,
)


def source():
    buffer = BytesIO()
    Image.new("RGB", (32, 40), "white").save(buffer, format="JPEG")
    return SourceFile(buffer.getvalue(), "image/jpeg", "SECRET-REFERENCE-NAME.jpg")


def response():
    return {"store": "Sklep", "date": "2026-10-25", "currency": "PLN",
            "total": "16.80", "amount_due": "18.80", "items": [{
                "name": "Piwo", "quantity": "4", "unit": "SZT", "unit_price": "6.50",
                "before_discount": "26.00", "discount": "-9.20", "total": "16.80",
                "category": "alkohol i tytoń", "needs_review": False, "review_reason": None}],
            "deposits": [{"name": "Kaucja", "quantity": "4", "unit_price": "0.50", "total": "2.00"}],
            "deposit_returns": [], "issues": [],
            "image_evidence": [{"field": "items[0]", "images": [1]}, {"field": "deposits[0].total", "images": [1]}]}


def test_request_preserves_original_bytes_and_redacts_reports():
    original = source()
    request = build_vision_request([original, original])
    text = json.dumps(request, ensure_ascii=False)
    assert original.name not in text
    images = [p for p in request["messages"][1]["content"] if p["type"] == "image_url"]
    assert len(images) == 2
    assert base64.b64decode(images[0]["image_url"]["url"].split(",")[1]) == original.content
    summary = request_summary(request)
    assert "base64," not in json.dumps(summary)
    assert "base64," in json.dumps(request)
    assert vision_bound() * 5 < 0.45


def test_money_and_deposits_are_kept_without_fabricated_ocr_evidence():
    raw = response()
    original = copy.deepcopy(raw)
    result, enrichment, evidence = decode_vision(raw, 1)
    assert raw == original
    assert str(result.output.items[0].total) == "16.80"
    assert str(result.output.deposits[0].total) == "2.00"
    assert str(result.output.amount_due) == "18.80"
    assert result.output.evidence == {}
    assert enrichment["items"][0]["category"] == "alkohol i tytoń"
    assert evidence == raw["image_evidence"]


@pytest.mark.parametrize("claim", [
    {"field": "items[0]", "images": [2]}, {"field": "items[9]", "images": [1]},
    {"field": "items[0].invented", "images": [1]}, {"field": "total", "images": [True]},
])
def test_invalid_image_claims_are_rejected(claim):
    raw = response()
    raw["image_evidence"] = [claim]
    with pytest.raises(ValueError):
        decode_vision(raw, 1)


def test_numeric_json_values_are_rejected_instead_of_silently_coerced():
    raw = response()
    raw["items"][0]["total"] = 16.80
    with pytest.raises(ValueError):
        decode_vision(raw, 1)


def test_group_image_reference_is_valid_without_changing_receipt():
    raw = response()
    raw["image_evidence"] = [{"field": "items", "images": [1, 2]}]
    result, _, claims = decode_vision(raw, 2)
    assert len(result.output.items) == 1 and result.output.evidence == {}
    assert claims == raw["image_evidence"]


def test_truncated_response_is_cached_but_never_accepted_or_retried(tmp_path):
    client = GemmaVisionParser("test-key", tmp_path)
    attempts = []
    def generate(body):
        attempts.append(body)
        return {"choices": [{"finish_reason": "length", "message": {"content": "{"}}]}
    client._generate = generate
    for _ in range(2):
        with pytest.raises(ValueError, match="truncated"):
            client.run([source()])
    assert len(attempts) == 1 and client.cache_hit
