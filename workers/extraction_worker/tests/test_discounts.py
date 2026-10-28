from decimal import Decimal

import pytest

from extraction.analysis.receipt_images import decode_vision
from extraction.contracts import AnalyzedReceipt, ItemAnnotation, ReceiptData, ReceiptItem
from extraction.steps.validate import ValidateAnalyzedReceiptStep, validate_receipt
from test_gemma_vision import response
from test_parsing import at, ocr, parse
from extraction.steps.parsing import ParseReceiptStep


@pytest.mark.parametrize("label", ["Opust za butelki", "Rabat na cały paragon", "Zniżka lojalnościowa"])
def test_independent_discount_remains_a_signed_item(label):
    result = parse(at("Napój 1 x5,00 5,00A", 0, 45, 400),
                   at(label, 0, 70, 200), at("-2,50", 300, 70), total="2,50")
    product, discount = result.output.items
    assert product.total == Decimal("5.00") and product.discount == 0
    assert discount.name == label and discount.total == Decimal("-2.50")
    assert discount.quantity is discount.unit_price is discount.before_discount is discount.discount is None
    assert result.output.evidence["items[1].total"].method == "observed"
    assert not validate_receipt(result.output)


def test_product_discount_and_independent_discount_are_counted_once():
    result = parse(at("Napój 1 x5,00 5,00A", 0, 45, 400),
                   at("OPUST Napój -0,50", 0, 65, 400),
                   at("4,50A", 300, 85),
                   at("Opust za butelki -2,50", 0, 110, 400), total="2,00")
    product, discount = result.output.items
    assert product.discount == Decimal("-0.50") and product.total == Decimal("4.50")
    assert discount.total == Decimal("-2.50")
    assert not validate_receipt(result.output)


def test_discount_description_in_deposit_section_stays_a_deposit_return():
    lines = [at('Sklep "ŻABKA"', 0, 0), at("PARAGON FISKALNY", 0, 20),
             at("Napój 1 x5,00 5,00A", 0, 45, 400), at("SUMA PLN 5,00", 0, 70),
             at("OPAKOWANIA PRZYJĘCIA", 0, 95), at("Opust za butelki -2,50", 0, 120, 400),
             at("DO ZAPŁATY 2,50", 0, 145), at("2026-10-07", 0, 170)]
    result = ParseReceiptStep().run(ocr(lines))
    assert len(result.output.items) == 1
    assert result.output.deposit_returns[0].total == Decimal("-2.50")
    assert not validate_receipt(result.output)


def test_independent_discount_before_first_purchase_is_kept():
    result = parse(at("Opust za butelki -2,50", 0, 45, 400),
                   at("Napój 1 x5,00 5,00A", 0, 75, 400), total="2,50")
    assert [item.total for item in result.output.items] == [Decimal("-2.50"), Decimal("5.00")]
    assert not validate_receipt(result.output)


def test_discount_summary_does_not_duplicate_independent_discount():
    result = parse(at("Napój 1 x5,00 5,00A", 0, 45, 400),
                   at("Opust za butelki -2,50", 0, 70, 400),
                   at("OPUSTY ŁĄCZNIE -2,50", 0, 95, 400), total="2,50")
    assert [item.total for item in result.output.items] == [Decimal("5.00"), Decimal("-2.50")]
    assert not validate_receipt(result.output)


def test_unreadable_standalone_discount_keeps_description_and_unknown_amount():
    result = parse(at("Napój 1 x5,00 5,00A", 0, 45, 400),
                   at("Opust za butelki", 0, 70, 200), total="5,00")
    assert result.output.items[1].name == "Opust za butelki"
    assert result.output.items[1].total is None
    assert any(w.code == "missing_discount" and w.field == "items[1].total" for w in result.warnings)


def test_vision_decoding_preserves_independent_discount_and_category():
    raw = response()
    raw["items"].append({"name": "Opust za butelki", "quantity": None, "unit": None,
                         "unit_price": None, "before_discount": None, "discount": None,
                         "total": "-2.50", "category": "zniżka", "needs_review": False,
                         "review_reason": None})
    raw["total"], raw["amount_due"] = "14.30", "16.30"
    decoded, enrichment, _ = decode_vision(raw, 1)
    assert decoded.output.items[1].total == Decimal("-2.50")
    assert enrichment["items"][1]["category"] == "zniżka"
    assert not validate_receipt(decoded.output)


def test_positive_discount_warns_without_rewriting_amounts():
    receipt = ReceiptData(items=[ReceiptItem(name="Rabat", total=Decimal("2.50"))])
    analyzed = AnalyzedReceipt(receipt, (ItemAnnotation(0, "Rabat", "Rabat", "zniżka", False, None),))
    result = ValidateAnalyzedReceiptStep().run(analyzed)
    assert any(w.code == "discount_sign_mismatch" and w.field == "items[0].total" for w in result.warnings)
    assert result.output.receipt.items[0].total == Decimal("2.50")
