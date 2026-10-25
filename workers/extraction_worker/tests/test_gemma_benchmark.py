import json
from decimal import Decimal

import pytest

from experiments.gemma_benchmark import Budget, chunk_receipt, make_challenges, score, validate_labels
from extraction.contracts import FieldEvidence, ReceiptData, ReceiptItem
from extraction.steps.enrich import decode_enrichment, decode_with_review_fallback, numeric_name_facts


def test_chunk_preserves_duplicate_items_and_remaps_name_evidence():
    receipt = ReceiptData(items=[ReceiptItem(name="A"), ReceiptItem(name="A"), ReceiptItem(name="B")],
                          evidence={"items[2].name": FieldEvidence(((1, 7),)), "store": FieldEvidence(((1, 0),))})
    chunk = chunk_receipt(receipt, 1, 3)
    assert [i.name for i in chunk.items] == ["A", "B"]
    assert chunk.evidence["items[1].name"].fragments == ((1, 7),)
    assert "items[2].name" not in chunk.evidence
    assert receipt.items[2].name == "B" and "items[2].name" in receipt.evidence


def test_budget_counts_failed_attempt_reservations_and_enforces_cap(tmp_path):
    budget = Budget(tmp_path, "0.01", 2)
    budget.reserve(Decimal("0.004"))
    budget.reserve(Decimal("0.004"))
    with pytest.raises(RuntimeError):
        budget.reserve(Decimal("0.001"))
    assert budget.calls == 2 and budget.reserved == Decimal("0.008")
    assert json.loads((tmp_path / "budget.json").read_text())["calls"] == 2
    budget.charge({"usage": {"cost": "0.0001"}})
    assert budget.cost == Decimal("0.0001")
    restored = Budget.restore(tmp_path)
    assert restored.calls == budget.calls and restored.cost == budget.cost
    with pytest.raises(RuntimeError):
        restored.reserve(Decimal("0.001"))


@pytest.mark.parametrize("raw,corrected,allowed", [
    ("CHIPSY D0RITO 180g", "CHIPSY DORITO 180g", True),
    ("NAPÓJ 180g", "NAPÓJ 180ml", False),
    ("PB95", "PB98", False), ("Ręcznik X2", "Ręcznik X3", False),
    ("MakSpagLub400g", "MakSpagLub900g", False),
    ("Woda 500m1", "Woda 500ml", True),
    ("Napój 0,5l", "Napój 0.5 l", True),
    ("KabelUSB2", "Kabel USB 2", True),
])
def test_numeric_guard_keeps_sizes_units_and_codes(raw, corrected, allowed):
    assert (numeric_name_facts(raw) == numeric_name_facts(corrected)) is allowed


def test_null_category_fallback_remains_valid_and_uncertain_name_is_not_guessed():
    receipt = ReceiptData(items=[ReceiptItem(name="Produkt 180g")])
    raw = {"items": [{"id": 0, "name_corrected": "Produkt 190g", "category": None,
                       "needs_review": True, "review_reason": "ambiguous_category"}]}
    item = decode_enrichment(raw, receipt)["items"][0]
    assert item["review_reason"] == "ambiguous_category"
    assert item["name_corrected"] == receipt.items[0].name
    del item["name_raw"]
    decode_enrichment({"items": [item]}, receipt)
    raw["items"][0].update(category="inne", review_reason="uncertain_name", name_corrected="Invented 180g")
    assert decode_enrichment(raw, receipt)["items"][0]["name_corrected"] == receipt.items[0].name
    raw["items"][0]["review_reason"] = "unreadable_name"
    with pytest.raises(ValueError):
        decode_enrichment(raw, receipt)


def test_failure_is_penalized_in_overall_accuracy_and_synthetic_cases_cover_all_categories():
    cases, labels = make_challenges()
    validate_labels(cases, labels)
    from extraction.categories import CATEGORIES
    assert set(CATEGORIES) <= {i["category"] for c in labels["cases"] for i in c["items"]}
    failed = [{**case, "status": "failed"} for case in cases]
    metrics = score(failed, labels)
    assert metrics["category_accuracy_all"] == 0
    assert metrics["category_accuracy"] is None


def test_resilient_decoder_quarantines_only_bad_fields_and_never_repairs_ids():
    receipt = ReceiptData(items=[ReceiptItem(name="Mleko"), ReceiptItem(name="Sok")])
    valid = {"id": 0, "name_corrected": "Mleko", "category": "żywność i napoje", "needs_review": False, "review_reason": None}
    invalid = {"id": 1, "name_corrected": "Sok", "category": "żywność i napoje", "needs_review": True, "review_reason": "unreadable_name"}
    result = decode_with_review_fallback({"items": [valid, invalid]}, receipt)
    assert result["items"][0]["needs_review"] is False
    assert result["items"][1]["category"] is None
    assert result["items"][1]["review_reason"] == "invalid_model_output"
    assert result["items"][1]["name_corrected"] == "Sok"
    invalid["id"] = 0
    with pytest.raises(ValueError):
        decode_with_review_fallback({"items": [valid, invalid]}, receipt)


def test_hybrid_takes_only_name_fields_from_name_pass():
    from experiments.gemma_benchmark import merge_independent_fields
    category = [{"source": "x", "parser": {"items": [{"name": "Mleko"}]}, "status": "completed", "enrichment": {"items": [
        {"id": 0, "name_raw": "Mleko", "name_corrected": "Mleko", "category": "żywność i napoje", "needs_review": False, "review_reason": None}]}}]
    import copy
    names = copy.deepcopy(category)
    names[0]["enrichment"]["items"][0].update(category="dom i ogród", name_corrected="Mleko", needs_review=True, review_reason="uncertain_name")
    result = merge_independent_fields(category, names)
    assert result[0]["enrichment"]["items"][0]["category"] == "żywność i napoje"
    assert result[0]["enrichment"]["items"][0]["needs_review"]
