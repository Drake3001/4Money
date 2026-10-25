import copy
import json
from decimal import Decimal

import pytest

from experiments.budget import SettledBudget
from experiments.gemma_multimodal import audit_study, receipt_score
from extraction.contracts import ImageOCRInput, SourceFiles
from extraction.steps.analysis import analyzed_receipt
from extraction.steps.parsing.vision import GemmaVisionParser, build_vision_request, decode_vision
from extraction.workflows import vision_ocr_analysis_pipeline
from test_analysis_workflows import envelope
from test_gemma_vision import response, source
from test_parsing import at, document, ocr


def test_known_billed_cost_releases_unused_bound_but_keeps_attempt_history(tmp_path):
    budget = SettledBudget(tmp_path, "0.10", 3)
    for _ in range(3):
        budget.reserve("0.08")
        budget.charge({"usage": {"cost": "0.001"}})
    assert budget.calls == 3 and budget.cost == budget.reserved == Decimal("0.003")
    raw = json.loads((tmp_path / "budget.json").read_text())
    assert Decimal(raw["cumulative_bounds_usd"]) == Decimal("0.24")
    restored = SettledBudget.restore(tmp_path)
    assert restored.calls == 3 and restored.reserved == Decimal("0.003")
    with pytest.raises(RuntimeError, match="call cap"):
        restored.reserve("0.01")


@pytest.mark.parametrize("response", [None, {}])
def test_failed_or_unknown_cost_attempt_keeps_full_bound(tmp_path, response):
    budget = SettledBudget(tmp_path, "0.10", 5)
    budget.reserve("0.08")
    if response is not None:
        budget.charge(response)
    assert budget.cost == 0 and budget.reserved == Decimal("0.08")
    with pytest.raises(RuntimeError):
        budget.reserve("0.08")
    assert SettledBudget.restore(tmp_path).calls == 1


def test_provider_overrun_is_recorded_and_stops_study(tmp_path):
    budget = SettledBudget(tmp_path, "0.10", 5)
    budget.reserve("0.01")
    with pytest.raises(RuntimeError, match="exceeded"):
        budget.charge({"usage": {"cost": "0.02"}})
    assert budget.cost == budget.reserved == Decimal("0.02")
    with pytest.raises(RuntimeError, match="uncharged"):
        budget.charge({"usage": {"cost": "0.02"}})


def test_image_plus_ocr_keeps_original_bytes_no_filenames_and_normalized_geometry():
    context = ocr(document(at("Mleko 1 x3,00 3,00A", 0, 50)))
    image = source()
    plain = build_vision_request([image])
    hybrid = build_vision_request([image], context)
    assert plain["messages"][1]["content"][2] == hybrid["messages"][1]["content"][2]
    encoded = json.dumps(hybrid, ensure_ascii=False)
    assert "Mleko 1 x3,00 3,00A" in encoded
    assert image.name not in encoded and context.pages[0].source_name not in encoded
    assert '"box"' in encoded.replace('\\"', '"')
    assert "Pomocniczy OCR" in hybrid["messages"][1]["content"][-1]["text"]
    with pytest.raises(ValueError, match="align"):
        build_vision_request([image, image], context)


def test_hybrid_uses_same_pipeline_output_and_preserves_image_provenance():
    client = GemmaVisionParser("test-key", request_policy=lambda body, send: envelope(response()))
    context = ocr(document(at("Piwo 4 x6,50", 0, 50)))
    result = vision_ocr_analysis_pipeline(client).run(ImageOCRInput(SourceFiles((source(),)), context))
    assert result.ok and result.output.annotations[0].category == "alkohol i tytoń"
    assert result.output.receipt.evidence == {} and result.output.image_evidence


def test_failure_is_scored_against_every_expected_item_and_category():
    reference = {"source": "test", **response()}
    score = receipt_score(reference)
    assert score["counts"]["completed"] == 0
    assert score["counts"]["categories_expected"] == score["counts"]["items_missing"] == 1
    assert score["evaluation"]["document_fields"]["total"]["status"] == "missing"


def test_vat_normalization_and_category_alternatives_do_not_change_primary_score():
    raw = response()
    result, enrichment, evidence = decode_vision(raw, 1)
    analysis = analyzed_receipt(result.output, enrichment, evidence)
    ref = {"source": "x", **raw}
    ref["items"][0] = {**ref["items"][0], "name": "Piwo-A", "tax_code": "A", "category": "inne",
                        "accepted_categories": ["inne", "alkohol i tytoń"]}
    score = receipt_score(ref, analysis)["counts"]
    assert score["categories_primary_correct"] == 0
    assert score["categories_accepted_correct"] == score["names_vat_normalized_correct"] == 1


def test_posthoc_audit_exposes_balanced_number_errors_and_null_fills_separately():
    from extraction.reporting import to_dict
    raw = response()
    reference = {"source": "x", **copy.deepcopy(raw)}
    reference["deposits"][0] = {**reference["deposits"][0], "quantity": None, "unit_price": None}
    raw["items"][0]["quantity"], raw["items"][0]["unit_price"] = "6.50", "4.00"
    decoded, enrichment, evidence = decode_vision(raw, 1)
    analysis = analyzed_receipt(decoded.output, enrichment, evidence)
    record = {"status": "completed", "analysis": to_dict(analysis), "validation": [], "score": receipt_score(reference, analysis)}
    case = {"source": "x", "reference": reference, **{m: record for m in ("pipeline", "vision", "vision_ocr")}}
    result = audit_study({"cases": [case]})["methods"]["vision_ocr"]
    assert len(result["different_known_numbers"]) == 2
    assert len(result["reference_null_fills"]) == 2
    assert result["unflagged_number_error_documents"] == ["x"]
    assert result["core_financial_exact_documents"] == []


def test_posthoc_cost_prefers_actual_new_chunk_cost_over_full_response_usage():
    record = {"status": "failed", "new_cost_usd": "0.001", "usage": {"cost": "0.009"}}
    cached = {"status": "failed", "cache_hit": True, "usage": {"cost": "0.009"}}
    report = {"methods": ["pipeline"], "cases": [{"pipeline": record}, {"pipeline": cached}]}
    assert audit_study(report)["methods"]["pipeline"]["new_cost_usd"] == "0.001"
