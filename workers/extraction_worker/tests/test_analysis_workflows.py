import copy
import json
from decimal import Decimal

import pytest

from experiments.budget import Budget
from extraction.contracts import (
    AnalyzedReceipt, ExtractionWarning, FieldEvidence, OCRResult, PreparedDocument,
    ReceiptData, ReceiptItem, SourceFiles, StepResult,
)
from extraction.reporting import to_dict, write_result
from extraction.requests import BudgetedRequests, cache_only
from extraction.steps.analysis import analyzed_receipt
from extraction.steps.enrich import PaidOpenRouterEnricher
from extraction.steps.parsing.vision import GemmaVisionParser
from extraction.workflows import text_analysis_pipeline, vision_analysis_pipeline
from test_gemma_vision import response as vision_answer, source


def envelope(raw):
    return {"choices": [{"finish_reason": "stop", "message": {"content": json.dumps(raw)}}],
            "usage": {"cost": "0.0001"}}


class FrozenParser:
    input_type = OCRResult
    output_type = ReceiptData
    version = "test"
    config = {}

    def __init__(self, receipt):
        self.receipt = receipt

    def run(self, input):
        return StepResult(self.receipt, (ExtractionWarning("parser_warning", "Keep this warning"),))


def test_text_pipeline_chunks_duplicates_preserves_money_and_provenance(tmp_path):
    receipt = ReceiptData(store="Sklep", total=Decimal("99.00"), items=[
        ReceiptItem(name="M1eko", quantity=Decimal("1"), unit_price=Decimal("3.00"),
                    before_discount=Decimal("3.00"), discount=Decimal("0.00"), total=Decimal("3.00"))
        for _ in range(9)], evidence={"items[8].name": FieldEvidence(((1, 0),))})
    before = copy.deepcopy(receipt)
    requests = []
    def policy(body, send):
        data = json.loads(json.loads(body)["messages"][1]["content"])
        requests.append(data)
        return envelope({"items": [{"id": item["id"], "name_corrected": "Mleko",
                         "category": "żywność i napoje", "needs_review": False, "review_reason": None}
                        for item in data["items"]]})
    client = PaidOpenRouterEnricher("secret-test-key", request_policy=policy)
    workflow = text_analysis_pipeline(client, parser=FrozenParser(receipt))
    ocr = OCRResult(PreparedDocument((), ()), (), "test", "1", {})
    result = workflow.run(ocr, capture_steps=True)
    assert result.ok and isinstance(result.output, AnalyzedReceipt)
    assert [len(r["items"]) for r in requests] == [8, 1]
    assert [a.id for a in result.output.annotations] == list(range(9))
    assert all(a.name_raw == "M1eko" and a.name_corrected == "Mleko" for a in result.output.annotations)
    assert receipt == before == result.output.receipt
    assert "items[8].name" in result.output.receipt.evidence
    assert {w.code for w in result.warnings} >= {"parser_warning", "total_mismatch"}
    assert [s.name for s in result.steps] == ["parse", "classify", "validate"]
    assert result.captured_steps["classify"] == result.output
    assert result.steps[1].implementation == "ClassifyReceiptItemsStep"
    write_result(tmp_path / "result", result, save_images=False)
    serialized = (tmp_path / "result/result.json").read_text(encoding="utf-8")
    assert '"annotations"' in serialized and "secret-test-key" not in serialized


def test_vision_pipeline_has_same_contract_and_validator_without_ocr():
    raw = vision_answer()
    raw["items"][0]["total"] = "17.80"
    client = GemmaVisionParser("test-key", request_policy=lambda body, send: envelope(raw))
    result = vision_analysis_pipeline(client).run(SourceFiles((source(),)))
    assert result.ok and isinstance(result.output, AnalyzedReceipt)
    assert result.output.receipt.items[0].total == Decimal("17.80")
    assert result.output.annotations[0].category == "alkohol i tytoń"
    assert result.output.image_evidence[0].images == (1,)
    assert result.output.receipt.evidence == {}
    assert {w.code for w in result.warnings} >= {"item_discount_mismatch", "total_mismatch"}


def test_enrichment_failure_stops_before_validator_without_partial_analysis():
    receipt = ReceiptData(items=[ReceiptItem(name="Mleko")])
    client = PaidOpenRouterEnricher("test-key", request_policy=lambda body, send: envelope({"items": []}))
    ocr = OCRResult(PreparedDocument((), ()), (), "test", "1", {})
    result = text_analysis_pipeline(client, parser=FrozenParser(receipt)).run(ocr)
    assert not result.ok and result.error.step == "classify"
    assert [step.name for step in result.steps] == ["parse", "classify"]
    assert not isinstance(result.output, AnalyzedReceipt)


def test_empty_parsed_receipt_makes_no_model_requests():
    client = PaidOpenRouterEnricher("test-key", request_policy=cache_only)
    ocr = OCRResult(PreparedDocument((), ()), (), "test", "1", {})
    result = text_analysis_pipeline(client, parser=FrozenParser(ReceiptData())).run(ocr)
    assert result.ok and result.output.annotations == ()
    assert "empty_items" in {w.code for w in result.warnings}


def test_annotations_cannot_silently_change_count_or_identity():
    receipt = ReceiptData(items=[ReceiptItem(name="Mleko")])
    with pytest.raises(ValueError, match="every receipt item"):
        analyzed_receipt(receipt, {"items": []})
    with pytest.raises(ValueError, match="identities"):
        analyzed_receipt(receipt, {"items": [{"id": 0, "name_raw": "Piwo", "name_corrected": "Piwo",
                         "category": "alkohol i tytoń", "needs_review": False, "review_reason": None}]})


def test_budget_is_applied_only_on_cache_miss_and_cache_only_never_sends(tmp_path):
    budget = Budget(tmp_path, "0.01", 1)
    policy = BudgetedRequests(budget, lambda body: Decimal("0.005"))
    sent = []
    def send(body):
        sent.append(body)
        return envelope(vision_answer())
    # Inject a fake transport underneath the real reservation policy.
    client = GemmaVisionParser("test-key", cache_dir=tmp_path / "cache",
                               request_policy=lambda body, network: policy(body, send))
    assert vision_analysis_pipeline(client).run(SourceFiles((source(),))).ok
    assert budget.calls == len(sent) == 1 and budget.cost == Decimal("0.0001")
    client.request_policy = cache_only
    assert vision_analysis_pipeline(client).run(SourceFiles((source(),))).ok
    assert client.cache_hit and budget.calls == 1
    uncached = GemmaVisionParser("test-key", request_policy=cache_only)
    result = vision_analysis_pipeline(uncached).run(SourceFiles((source(),)))
    assert not result.ok and "network is forbidden" in result.error.message
    assert result.error.step == "analyze"
    assert [step.name for step in result.steps] == ["analyze"]


def test_budget_counts_failed_transport_and_blocks_next_send(tmp_path):
    budget = Budget(tmp_path, "0.01", 1)
    policy = BudgetedRequests(budget, lambda body: Decimal("0.005"))
    attempts = []
    def fail(body):
        attempts.append(body)
        raise TimeoutError("offline simulated timeout")
    with pytest.raises(TimeoutError):
        policy(b"{}", fail)
    with pytest.raises(RuntimeError, match="budget/call cap"):
        policy(b"{}", fail)
    assert len(attempts) == budget.calls == 1 and budget.cost == 0


def test_budget_ledger_cannot_be_reset_by_constructing_a_new_budget(tmp_path):
    budget = Budget(tmp_path, "0.01", 1)
    budget.reserve(Decimal("0.005"))
    with pytest.raises(FileExistsError, match="restore"):
        Budget(tmp_path, "0.01", 1)
    restored = Budget.restore(tmp_path)
    assert restored.calls == 1 and restored.reserved == Decimal("0.005")


@pytest.mark.parametrize("amount", ["-0.01", "NaN", "Infinity"])
def test_invalid_reservation_never_spends_a_call(tmp_path, amount):
    budget = Budget(tmp_path, "0.01", 1)
    with pytest.raises(ValueError):
        budget.reserve(amount)
    assert budget.calls == 0 and budget.reserved == 0


def test_canonical_result_can_be_evaluated_in_memory_and_from_json():
    from experiments.parsing import evaluate_receipt
    receipt = ReceiptData(total=Decimal("3.00"), items=[ReceiptItem(name="Mleko", total=Decimal("3.00"))])
    analysis = analyzed_receipt(receipt, {"items": [{"id": 0, "name_raw": "Mleko", "name_corrected": "Mleko",
                                "category": "żywność i napoje", "needs_review": False, "review_reason": None}]})
    ref = {"source": "a.jpg", **to_dict(receipt)}
    assert evaluate_receipt(ref, analysis) == evaluate_receipt(ref, to_dict(analysis))
    assert evaluate_receipt(ref, analysis)["groups"]["items"]["matched_count"] == 1


def test_main_explicit_vision_client_builds_no_ocr_steps():
    import main
    client = GemmaVisionParser("test-key", request_policy=cache_only)
    assert [name for name, step in main.build_pipeline(vision_client=client).steps] == ["analyze", "validate"]
    with pytest.raises(ValueError):
        main.build_pipeline(enricher=object(), vision_client=client)
