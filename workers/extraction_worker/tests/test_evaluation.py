import json

import pytest
from experiments.evaluation import audit_receipt, normalize_name, write_evaluation
from extraction.contracts import (
    OCRFragment, OCRPage, OCRResult, PipelineError, PipelineResult,
    PreparedDocument, ReceiptData,
)
from extraction.reporting import to_dict


def line(text, y, x=10):
    return {
        "text": text,
        "confidence": 0.99,
        "box": [[x, y], [x + 100, y], [x + 100, y + 10], [x, y + 10]],
    }


def reference(items=None):
    return {
        "source": "receipt.png",
        "date": "2026-10-10",
        "time": "12:00",
        "total": "3.95",
        "amount_due": "3.95",
        "uncertain_fields": [],
        "items": items
        if items is not None
        else [
            {
                "name": "Napój mlecz.",
                "quantity": "1",
                "unit_price": "3.95",
                "before_discount": "3.95",
                "discount": "0.00",
                "total": "3.95",
                "printed_marker": "F",
            }
        ],
    }


def raw(lines):
    return {
        "engine": "test",
        "version": "1",
        "settings": {},
        "pages": [{"page_number": 1, "lines": lines}],
    }


def test_unprinted_time_is_not_counted_as_missing_ocr_value():
    data = reference()
    data["time"] = None
    data["unprinted_fields"] = ["time"]
    result = audit_receipt(data, raw([line("2026-10-10", 10)]))
    assert result["document_values"]["time"] == {
        "expected": None, "present_in_document": None, "status": "not_printed",
    }
    assert result["document_values"]["date"]["present_in_document"] is True


def test_computed_gross_is_excluded_from_ocr_field_counts():
    data = reference()
    data["items"][0]["before_discount_is_printed"] = False
    result = audit_receipt(data, raw([line("Napój mlecz. 1 x3,95 3,95", 10)]))
    checks = result["items"][0]["fields"]
    assert checks["before_discount"]["status"] == "not_printed"
    assert checks["total"]["status"] == "match"


def test_exact_name_and_purchase_amounts_ignore_spacing_and_marker():
    result = audit_receipt(
        reference(), raw([line("NAPÓJ MLECZ. F", 10), line("1 x3,95 3,95C", 10, 200)])
    )
    item = result["items"][0]
    assert item["name_status"] == "match"
    assert all(check["status"] == "match" for check in item["fields"].values())


def test_polish_letters_are_not_erased_by_name_normalization():
    assert normalize_name("Łosoś XXL") != normalize_name("Losos XXL")
    data = reference()
    data["items"][0]["name"] = "Łosoś XXL"
    result = audit_receipt(data, raw([line("Losos XXL", 10)]))
    assert result["items"][0]["name_status"] == "approximate"


def test_expected_amount_elsewhere_does_not_fix_wrong_item():
    result = audit_receipt(
        reference(),
        raw(
            [
                line("Napój mlecz. F", 10),
                line("2 x3,95 7,90C", 10, 200),
                line("SUMA PLN 3,95", 90),
            ]
        ),
    )
    fields = result["items"][0]["fields"]
    assert fields["quantity"]["status"] == "different"
    assert fields["total"]["status"] == "different"
    assert result["document_values"]["total"]["present_in_document"] is True


def test_discount_line_is_not_counted_as_product_name():
    result = audit_receipt(reference(), raw([line("OPUST Napój mlecz. F", 10)]))
    assert result["items"][0]["name_status"] == "missing"
    assert all(
        check["status"] == "missing" for check in result["items"][0]["fields"].values()
    )


def test_one_fragment_cannot_count_as_two_products():
    data = reference()
    data["items"].append(dict(data["items"][0]))
    result = audit_receipt(data, raw([line("Napój mlecz. F", 10)]))
    assert result["name_counts"] == {"missing": 1, "match": 1}


def test_discount_and_final_amount_are_separate_from_gross():
    data = reference()
    data["items"][0].update(discount="-0.95", total="3.00")
    result = audit_receipt(
        data,
        raw(
            [
                line("Napój mlecz. F", 10),
                line("1 x3,95 3,95C", 10, 200),
                line("OPUST Napój mlecz. F", 35),
                line("-0,95", 35, 200),
                line("3,00C", 55, 200),
                line("Podsuma: 3,00", 80),
                line("PTU 0,50", 100),
            ]
        ),
    )
    fields = result["items"][0]["fields"]
    assert fields["before_discount"]["observed"] == ["3.95"]
    assert fields["discount"]["status"] == "match"
    assert fields["total"]["status"] == "match"
    assert fields["total"]["observed"] == ["3.00"]


def test_conflicting_numbers_are_ambiguous_even_when_expected_is_present():
    result = audit_receipt(
        reference(),
        raw(
            [
                line("Napój mlecz. F", 10),
                line("1 x3,95 3,95C", 10, 200),
                line("1 x3,95 5,00C", 10, 350),
            ]
        ),
    )
    assert result["items"][0]["fields"]["total"]["status"] == "ambiguous"


def test_printed_quantity_unit_is_supported():
    result = audit_receipt(
        reference(),
        raw(
            [
                line("Napój mlecz. F", 10),
                line("1szt. x3,95 3,95A", 10, 200),
            ]
        ),
    )
    assert result["items"][0]["fields"]["quantity"]["status"] == "match"


def test_tilted_columns_do_not_borrow_previous_purchase():
    data = reference()
    data["items"].append(
        {
            **data["items"][0],
            "name": "Bułka",
            "unit_price": "2.00",
            "before_discount": "2.00",
            "total": "2.00",
        }
    )

    def tilted(text, x, y):
        return {
            "text": text,
            "confidence": 0.99,
            "box": [[x, y], [x + 200, y + 20], [x + 200, y + 40], [x, y + 20]],
        }

    result = audit_receipt(
        data,
        raw(
            [
                tilted("Napój mlecz. F", 0, 10),
                tilted("1 x3,95 3,95C", 400, 50),
                tilted("Bułka", 0, 40),
                tilted("1 x2,00 2,00C", 400, 80),
            ]
        ),
    )
    assert [item["fields"]["total"]["observed"] for item in result["items"]] == [
        ["3.95"],
        ["2.00"],
    ]


def test_report_skips_missing_ocr_and_cannot_overwrite(tmp_path):
    refs, run = tmp_path / "photos", tmp_path / "run"
    refs.mkdir()
    run.mkdir()
    (refs / "receipt.expected.json").write_text(
        json.dumps(reference()), encoding="utf-8"
    )
    (refs / "missing.expected.json").write_text(
        json.dumps({**reference(), "source": "missing.png"}), encoding="utf-8"
    )
    (run / "receipt.png").mkdir()
    (run / "receipt.png/result.json").write_text(
        json.dumps({"output": {"kind": "ocr", **raw([line("Napój mlecz. F", 10)])}}),
        encoding="utf-8",
    )
    output = write_evaluation(run, refs)
    report = json.loads((output.parent / "report.json").read_text(encoding="utf-8"))
    assert len(report["cases"]) == 1 and len(report["skipped"]) == 1
    assert "nie wynik parsera" in output.read_text(encoding="utf-8")
    before = output.read_bytes()
    with pytest.raises(FileExistsError):
        write_evaluation(run, refs)
    assert output.read_bytes() == before


def test_no_references_do_not_create_output(tmp_path):
    assert write_evaluation(tmp_path, tmp_path) is None
    assert not (tmp_path / "evaluation").exists()


def intermediate_ocr():
    fragments = tuple(
        OCRFragment(value["text"], value["confidence"], tuple(map(tuple, value["box"])))
        for value in [line("Napój mlecz. F", 10), line("1 x3,95 3,95C", 10, 200)]
    )
    return OCRResult(
        PreparedDocument((), ()),
        (OCRPage(1, "receipt.png", 1, 1, 500, 500, fragments),),
        "test", "1", {},
    )


@pytest.mark.parametrize("failed", [False, True])
def test_ocr_audit_uses_intermediate_after_parsing(tmp_path, failed):
    refs, run = tmp_path / "refs", tmp_path / "run"
    refs.mkdir()
    run.mkdir()
    (refs / "receipt.expected.json").write_text(json.dumps(reference()), encoding="utf-8")
    result = PipelineResult(
        output=ReceiptData(),
        captured_steps={"recognize": intermediate_ocr()},
        error=PipelineError("parse", "ValueError", "failed") if failed else None,
    )
    output = write_evaluation(run, refs, {"receipt.png": result})
    report = json.loads(output.with_suffix(".json").read_text(encoding="utf-8"))
    assert len(report["cases"]) == 1 and report["skipped"] == []
    assert report["cases"][0]["numeric_field_counts"] == {"match": 4}


def test_ocr_audit_replays_saved_intermediate_without_memory_result(tmp_path):
    refs, run = tmp_path / "refs", tmp_path / "run"
    refs.mkdir()
    (run / "receipt.png").mkdir(parents=True)
    (refs / "receipt.expected.json").write_text(json.dumps(reference()), encoding="utf-8")
    (run / "receipt.png/result.json").write_text(
        json.dumps({"output": {"items": []}}), encoding="utf-8"
    )
    (run / "receipt.png/ocr.json").write_text(
        json.dumps(to_dict(intermediate_ocr())), encoding="utf-8"
    )
    output = write_evaluation(run, refs)
    report = json.loads(output.with_suffix(".json").read_text(encoding="utf-8"))
    assert len(report["cases"]) == 1 and report["skipped"] == []


def test_aldi_star_and_reference_tax_suffix_are_supported():
    data = reference()
    data["items"][0].update(name="REKLAMÓWKA T-SHIRT", tax_code="A")
    result = audit_receipt(data, raw([line("REKLAMÓWKA T-SHIRT A", 10), line("1SZT * 3,95 3,95 A", 10, 200)]))
    assert result["items"][0]["name_status"] == "match"
    assert result["numeric_field_counts"] == {"match": 4}
    wrong = audit_receipt(data, raw([line("REKLAMÓWKA T-SHIRT B", 10)]))
    assert wrong["items"][0]["name_status"] == "approximate"


def test_split_purchase_fragments_are_joined_without_borrowing_next_row():
    data = reference()
    data["items"].append({**data["items"][0], "name": "Bułka", "unit_price": "2.00", "before_discount": "2.00", "total": "2.00"})
    result = audit_receipt(data, raw([
        line("Napój mlecz. F", 10), line("1szt. x3,95", 12, 200), line("3,95C", 12, 350),
        line("Bułka", 30), line("1 x2,00 2,00C", 30, 200)]))
    assert [item["fields"]["total"]["observed"] for item in result["items"]] == [["3.95"], ["2.00"]]


def test_unprinted_total_is_derived_only_from_observed_numbers():
    data = reference()
    data["items"][0].update(discount="-0.95", total="3.00", total_is_printed=False)
    result = audit_receipt(data, raw([line("Napój mlecz. F", 10), line("1 x3,95 3,95C", 10, 200), line("OPUST -0,95", 35)]))
    item = result["items"][0]
    assert item["fields"]["total"]["status"] == "not_printed"
    assert item["computed_total"]["observed"] == ["3.00"]
    assert result["numeric_field_counts"] == {"match": 4}
    wrong = audit_receipt(data, raw([line("Napój mlecz. F", 10), line("1 x3,95 3,95C", 10, 200), line("OPUST -0,90", 35)]))
    assert wrong["items"][0]["computed_total"]["status"] == "different"


def test_merchant_is_matched_only_in_header_and_keeps_diacritic_errors():
    data = {**reference(), "store": "Żabka"}
    result = audit_receipt(data, raw([line('Sklep "ŻABKA" nr Z123', 0), line("PARAGON FISKALNY", 15), line("Napój mlecz. F", 30)]))
    assert result["merchant"]["status"] == "match"
    approximate = audit_receipt(data, raw([line('Sklep "ZABKA"', 0), line("PARAGON FISKALNY", 15), line("Napój mlecz. F", 30)]))
    assert approximate["merchant"]["status"] == "approximate"
    footer = audit_receipt(data, raw([line("PARAGON FISKALNY", 0), line("Napój mlecz. F", 30), line("ŻABKA reklama", 100)]))
    assert footer["merchant"]["status"] == "missing"
