import json
from datetime import date
from decimal import Decimal

import pytest

from experiments.parsing import _restore_ocr, evaluate_receipt, replay_saved_ocr, write_parser_evaluation
from extraction.contracts import PipelineResult, ReceiptData, ReceiptDeposit, ReceiptItem
from extraction.reporting import to_dict
from extraction.steps.validate import validate_receipt


def item(name="Napój", amount="3.95"):
    return {"name": name, "quantity": "1", "unit_price": amount, "before_discount": amount, "discount": "0.00", "total": amount}


def reference(items=None):
    return {
        "source": "receipt.png", "review_status": "pending_user_review",
        "uncertain_fields": [{"field": "items[0].name", "detail": "faint print"}],
        "store": "Żabka", "date": "2026-10-10", "currency": "PLN",
        "total": "3.95", "amount_due": "3.95", "items": [item()] if items is None else items,
    }


def test_alignment_preserves_duplicates_and_counts_missing_and_excess():
    expected = reference([item("Napój"), item("Napój"), item("Bułka", "2.00")])
    observed = {**expected, "items": [item("Napój"), item("Inny produkt", "99.00"), item("Bułka", "2.00")]}
    group = evaluate_receipt(expected, observed)["groups"]["items"]
    assert group["matched_count"] == 2
    assert group["missing_indices"] == [1]
    assert group["excess_indices"] == [1]
    assert [entry["observed_index"] for entry in group["entries"]] == [0, None, 2]


def test_fuzzy_names_do_not_mask_wrong_numeric_fields_or_missing_names():
    expected = reference([item("Łosoś XXL")])
    observed = {**expected, "items": [item("Losos XXL", "5.00")]}
    fields = evaluate_receipt(expected, observed)["groups"]["items"]["entries"][0]["fields"]
    assert fields["name"]["status"] == "approximate"
    assert fields["total"]["status"] == "different"
    observed["items"] = [item(None)]
    fields = evaluate_receipt(expected, observed)["groups"]["items"]["entries"][0]["fields"]
    assert fields["name"]["status"] == "missing"
    assert fields["total"]["status"] == "match"


def test_decimal_comparison_and_unannotated_values():
    expected = reference()
    observed = {**expected, "total": "3.950", "amount_due": None}
    observed["items"] = [{**item(), "quantity": "1.0", "discount": "NaN"}]
    case = evaluate_receipt(expected, observed)
    assert case["document_fields"]["total"]["status"] == "match"
    assert case["document_fields"]["amount_due"]["status"] == "missing"
    assert case["groups"]["items"]["entries"][0]["fields"]["discount"]["status"] == "different"
    assert case["review_status"] == "pending_user_review"
    assert case["reference_uncertainties"] == expected["uncertain_fields"]


def test_report_retains_observed_and_derived_field_evidence():
    expected = reference()
    observed = {
        **expected,
        "evidence": {
            "total": {"fragments": [[1, 4]], "method": "observed"},
            "items[0].total": {"fragments": [[1, 1], [1, 2]], "method": "derived"},
        },
    }
    case = evaluate_receipt(expected, observed)
    assert case["document_fields"]["total"]["evidence"]["method"] == "observed"
    item_fields = case["groups"]["items"]["entries"][0]["fields"]
    assert item_fields["total"]["evidence"] == observed["evidence"]["items[0].total"]


def test_unannotated_defaults_are_not_counted_as_parser_errors():
    expected = {"source": "receipt.png", "items": [{"name": "Napój", "total": "3.95"}]}
    observed = {**reference(), "deposits": [{"name": "Kaucja", "total": "0.50"}]}
    case = evaluate_receipt(expected, observed)
    assert all(check["status"] == "not_annotated" for check in case["document_fields"].values())
    fields = case["groups"]["items"]["entries"][0]["fields"]
    assert fields["discount"]["status"] == "not_annotated"
    assert fields["before_discount"]["status"] == "not_annotated"
    assert case["groups"]["deposits"]["annotated"] is False
    assert case["groups"]["deposits"]["excess_indices"] == []
    expected["deposits"] = []
    assert evaluate_receipt(expected, observed)["groups"]["deposits"]["excess_indices"] == [0]


@pytest.mark.parametrize("box,score", [([[0, 0]], 0.9), ([[0, 0], [1, 0], [1, float("nan")], [0, 1]], 0.9), ([[0, 0], [1, 0], [1, 1], [0, 1]], float("inf"))])
def test_saved_ocr_rejects_invalid_geometry_and_confidence(box, score):
    raw = {
        "document": {"sources": [], "pages": []},
        "engine": "rapidocr", "version": "saved", "settings": {},
        "pages": [{
            "page_number": 1, "source_name": "receipt.png", "source_index": 1,
            "source_page_number": 1, "width": 100, "height": 100,
            "lines": [{"text": "receipt", "confidence": score, "box": box}],
        }],
    }
    with pytest.raises(ValueError, match="four 2D points|non-finite"):
        _restore_ocr(raw)


def test_report_uses_predictions_and_separates_reviewed_references(tmp_path):
    inputs, run = tmp_path / "input", tmp_path / "run"
    inputs.mkdir()
    run.mkdir()
    expected = reference()
    (inputs / "receipt.expected.json").write_text(json.dumps(expected), encoding="utf-8")
    data = ReceiptData(
        store="Żabka", date=date(2026, 10, 10), currency="PLN", total=Decimal("3.95"), amount_due=Decimal("3.95"),
        items=[ReceiptItem(name="Napój", quantity=Decimal("1"), unit_price=Decimal("3.95"), total=Decimal("3.95"))],
    )
    report_path = write_parser_evaluation(run, inputs, {"receipt.png": PipelineResult(output=data)})
    report = json.loads(report_path.with_suffix(".json").read_text(encoding="utf-8"))
    assert report["summary"]["groups"]["items"]["matched_count"] == 1
    assert report["reviewed_summary"]["documents"] == 0
    assert report["cases"][0]["document_fields"]["total"]["status"] == "match"
    assert not (run / "evaluation").exists()
    assert write_parser_evaluation(run, tmp_path / "absent") is None


def test_report_rejects_reference_path_traversal(tmp_path):
    inputs, run = tmp_path / "input", tmp_path / "run"
    inputs.mkdir()
    run.mkdir()
    (inputs / "bad.expected.json").write_text(json.dumps({**reference(), "source": "../secret"}), encoding="utf-8")
    report_path = write_parser_evaluation(run, inputs)
    report = json.loads(report_path.with_suffix(".json").read_text(encoding="utf-8"))
    assert not report["cases"] and len(report["skipped"]) == 1


@pytest.mark.parametrize("sources,accepted", [(["01.png", "02.jpg"], True), (["01.png"], False), (["02.jpg", "01.png"], False)])
def test_report_rejects_stale_or_reordered_grouped_references(tmp_path, sources, accepted):
    inputs, run = tmp_path / "input", tmp_path / "run"
    group = inputs / "grouped-receipt"
    group.mkdir(parents=True)
    run.mkdir()
    for filename in ("02.jpg", "01.png", "notes.txt"):
        (group / filename).write_bytes(b"fixture")
    expected = {**reference(), "source": group.name, "sources": sources}
    (inputs / "grouped-receipt.expected.json").write_text(json.dumps(expected), encoding="utf-8")
    result = PipelineResult(output=ReceiptData())
    report_path = write_parser_evaluation(run, inputs, {group.name: result})
    report = json.loads(report_path.with_suffix(".json").read_text(encoding="utf-8"))
    assert bool(report["cases"]) is accepted
    assert bool(report["skipped"]) is not accepted
    if not accepted:
        assert "ordered files" in report["skipped"][0]["reason"]


@pytest.mark.parametrize("filename,wrapped", [("result.json", True), ("ocr.json", False)])
def test_saved_ocr_replay_keeps_geometry_and_metadata_without_images_or_ocr(tmp_path, filename, wrapped):
    from extraction.steps.parsing import ParseReceiptStep

    source = tmp_path / "saved" / "receipt.png"
    source.mkdir(parents=True)
    raw = {
        "kind": "ocr", "engine": "rapidocr", "version": "saved-v1", "settings": {"test": True},
        "document": {"sources": [{"name": "receipt.png", "mime_type": "image/png"}], "pages": [{"number": 1, "source_name": "receipt.png", "source_index": 1, "source_page_number": 1, "width": 100, "height": 200, "mime_type": "image/png"}]},
        "pages": [{"page_number": 1, "source_name": "receipt.png", "source_index": 1, "source_page_number": 1, "width": 100, "height": 200, "lines": [{"text": "PARAGON FISKALNY", "confidence": 0.9, "box": [[1, 2], [90, 3], [90, 10], [1, 9]]}]}],
    }
    (source / filename).write_text(json.dumps({"output": raw} if wrapped else raw), encoding="utf-8")
    # Only the parser is replaced: the replay must reconstruct the exact saved input.
    captured = []
    from extraction.contracts import StepResult
    from unittest.mock import patch

    def parse(self, value):
        captured.append(value)
        return StepResult(ReceiptData(total=Decimal("0")))

    with patch.object(ParseReceiptStep, "run", parse):
        results = replay_saved_ocr(source.parent, tmp_path / "no-references", tmp_path / "replay")
    assert results["receipt.png"].ok
    ocr = captured[0]
    assert ocr.engine == "rapidocr" and ocr.version == "saved-v1"
    assert ocr.pages[0].lines[0].box[1] == (90, 3)
    assert ocr.document.pages[0].content == b""
    assert ocr.document.sources[0].content == b""
    saved = json.loads((tmp_path / "replay" / "receipt.png" / "ocr.json").read_text(encoding="utf-8"))
    assert saved["pages"] == raw["pages"]
    assert saved["document"] == raw["document"]
    assert (tmp_path / "replay" / "receipt.png" / "ocr.txt").is_file()
    assert not (tmp_path / "replay" / "receipt.png" / "debug").exists()


def test_validation_uses_signed_returns_and_does_not_mutate_data():
    data = ReceiptData(
        total=Decimal("44.89"), amount_due=Decimal("17.89"),
        items=[ReceiptItem(total=Decimal("44.89"))],
        deposits=[ReceiptDeposit(total=Decimal("1.00"))],
        deposit_returns=[ReceiptDeposit(total=Decimal("-28.00"))],
    )
    before = to_dict(data)
    assert not {warning.code for warning in validate_receipt(data)}.intersection({"total_mismatch", "amount_due_mismatch"})
    assert to_dict(data) == before
    data.amount_due = Decimal("18")
    assert "amount_due_mismatch" in {warning.code for warning in validate_receipt(data)}
    data.deposit_returns[0].total = None
    assert "amount_due_mismatch" not in {warning.code for warning in validate_receipt(data)}


@pytest.mark.parametrize("field", ["amount_due", "discount", "deposit_total"])
def test_nonfinite_parser_fields_are_reported_without_decimal_errors(field):
    data = ReceiptData(total=Decimal("1"), amount_due=Decimal("1"), items=[ReceiptItem(total=Decimal("1"))])
    if field == "amount_due":
        data.amount_due = Decimal("sNaN")
    elif field == "discount":
        data.items[0].discount = Decimal("Infinity")
    else:
        data.deposits = [ReceiptDeposit(total=Decimal("NaN"))]
    assert "invalid_number" in {warning.code for warning in validate_receipt(data)}


def test_item_arithmetic_rounds_weighted_prices_and_checks_discount():
    data = ReceiptData(items=[ReceiptItem(
        quantity=Decimal("0.224"), unit_price=Decimal("14.99"),
        before_discount=Decimal("3.36"), discount=Decimal("-0.23"), total=Decimal("3.13"),
    )])
    assert not {warning.code for warning in validate_receipt(data)}.intersection({"item_price_mismatch", "item_discount_mismatch"})
    data.items[0].before_discount = Decimal("4.00")
    codes = {warning.code for warning in validate_receipt(data)}
    assert {"item_price_mismatch", "item_discount_mismatch"} <= codes


def test_deposit_sign_validation_keeps_raw_amounts():
    data = ReceiptData(
        deposits=[ReceiptDeposit(total=Decimal("-0.50"))],
        deposit_returns=[ReceiptDeposit(total=Decimal("6"))],
    )
    warnings = [warning for warning in validate_receipt(data) if warning.code == "deposit_sign_mismatch"]
    assert {warning.field for warning in warnings} == {"deposits[0].total", "deposit_returns[0].total"}
    assert data.deposits[0].total == Decimal("-0.50")
