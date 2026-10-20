"""Layout and amount ownership regressions without merchant or photo fixtures."""

from dataclasses import replace
from decimal import Decimal

import pytest

from extraction.contracts import OCRFragment
from extraction.steps.parsing import ParseReceiptStep
from test_parsing import at, document, ocr, parse


@pytest.mark.parametrize("turns", [1, 2, 3])
def test_sideways_and_upside_down_boxes_keep_amounts_and_original_refs(turns):
    original = ocr(document(at("Napój", 0, 50), at("2 x3,00 6,00C", 250, 50), total="6,00"))
    page = original.pages[0]
    def rotate(point):
        x, y = point
        if turns == 1:
            return page.height-y, x
        if turns == 2:
            return page.width-x, page.height-y
        return y, page.width-x
    fragments = []
    for line in page.lines:
        points = [rotate(point) for point in line.box]
        offset = (-turns) % 4
        fragments.append(replace(line, box=tuple(points[offset:] + points[:offset])))
    rotated = replace(original, pages=(replace(
        page, width=page.height if turns % 2 else page.width,
        height=page.width if turns % 2 else page.height, lines=tuple(reversed(fragments))),))
    before = rotated.pages[0].lines
    result = ParseReceiptStep().run(rotated)
    assert [item.total for item in result.output.items] == [Decimal("6.00")]
    assert result.output.total == Decimal("6.00")
    assert rotated.pages[0].lines == before
    refs = result.output.evidence["items[0].unit_price"].fragments
    assert any("2 x3,00" in before[index].text for _, index in refs)


@pytest.mark.parametrize("marker", ["PARAGON FISKAI NY", "PARAGON FISKALNV", "DOARAGON FISKALNY"])
def test_fiscal_heading_tolerates_small_ocr_errors(marker):
    lines = document(at("Produkt 1 x3,00 3,00A", 0, 50), total="3,00")
    lines[1] = at(marker, 0, 20)
    result = ParseReceiptStep().run(ocr(lines))
    assert result.output.items[0].total == Decimal("3.00")


def test_purchase_sharing_heading_row_is_not_discarded():
    result = ParseReceiptStep().run(ocr([
        at("PARAGON FISKALNY", 180, 20), at("Produkt", 0, 20),
        at("1 x3,00 3,00A", 450, 20), at("SUMA PLN 3,00", 0, 60),
    ]))
    assert len(result.output.items) == 1
    assert result.output.items[0].total == Decimal("3.00")


def test_missing_heading_requires_seller_purchases_and_total():
    result = ParseReceiptStep().run(ocr([
        at("NIP 1234567890", 0, 0), at("Produkt 1 x3,00 3,00A", 0, 30),
        at("SUMA PLN 3,00", 0, 60),
    ]))
    assert result.output.items[0].total == Decimal("3.00")
    assert "inferred_receipt_section" in {w.code for w in result.warnings}


def test_locally_sloped_names_do_not_borrow_neighboring_prices():
    def name(text, y):
        return OCRFragment(text, 0.99, ((0, y), (100, y+10), (100, y+20), (0, y+10)))
    result = parse(name("Pierwszy", 45), at("1 x3,00 3,00A", 300, 62.5),
                   name("Drugi", 80), at("2 x4,00 8,00A", 300, 97.5), total="11,00")
    assert [(item.name, item.unit_price) for item in result.output.items] == [
        ("Pierwszy", Decimal("3.00")), ("Drugi", Decimal("4.00"))]


def test_unsigned_discount_is_confirmed_by_gross_and_printed_net():
    result = parse(at("Produkt 1 x8,00 8,00A", 0, 45, 400),
                   at("OPUST Produkt", 0, 65), at("2,00", 300, 65),
                   at("6,00A", 300, 85), total="6,00")
    item = result.output.items[0]
    assert (item.discount, item.total) == (Decimal("-2.00"), Decimal("6.00"))
    assert result.output.evidence["items[0].discount"].method == "derived"


def test_unreadable_discount_does_not_turn_net_into_another_discount():
    result = parse(at("Produkt 1 x8,00 8,00A", 0, 45, 400),
                   at("OPUST -?,??", 0, 65), at("6,00A", 300, 85), total="6,00")
    assert result.output.items[0].total == Decimal("6.00")
    assert result.output.items[0].discount == Decimal("-2.00")


def test_plural_discount_summary_is_not_applied_again_despite_ocr_typo():
    result = parse(at("Produkt 1 x8,00 8,00A", 0, 45, 400),
                   at("OPUST -2,00", 0, 65), at("6,00A", 300, 85),
                   at("OPUSTY EACZNIE -2,00", 0, 105), total="6,00")
    assert result.output.items[0].total == Decimal("6.00")


def test_missing_multiplier_keeps_printed_gross_without_inventing_unit_price():
    result = parse(at("Produkt 2?3,00 6,00A", 0, 45, 400), total="6,00")
    item = result.output.items[0]
    assert item.quantity is item.unit_price is None
    assert item.before_discount == item.total == Decimal("6.00")


def test_zero_precision_tail_does_not_require_changing_a_digit():
    result = parse(at("Produkt 1 x8,00 8,00A", 0, 45, 400),
                   at("OPUST -2,00", 0, 65), at("6,000", 300, 85), total="6,00")
    assert result.output.items[0].total == Decimal("6.00")
    assert result.output.evidence["items[0].total"].method == "observed"


def test_single_item_discount_after_subtotal_has_its_own_evidence():
    result = ParseReceiptStep().run(ocr([
        at("PARAGON FISKALNY", 0, 20), at("Paliwo 2 LTR *4,00 8,00B", 0, 45, 400),
        at("PODSUMA 8,00", 0, 65), at("OPUST NADPLATA -0,03", 0, 85),
        at("OPUSTY LACZNIE -0,03", 0, 105), at("SUMA PLN 7,97", 0, 130),
    ]))
    assert result.output.items[0].total == Decimal("7.97")
    assert result.output.items[0].discount == Decimal("-0.03")
    assert result.output.evidence["items[0].total"].method == "derived"
    assert (1, 3) in result.output.evidence["items[0].total"].fragments


def test_confirmation_table_separates_net_tax_and_gross():
    result = ParseReceiptStep().run(ocr([
        at("NIEFISKALNY POTWIERDZENIE TRANSAKCJI", 0, 0, 400),
        at("NIP 1234567890", 0, 20), at("Data 2026-10-01", 0, 40),
        at("Paliwo 2 L*4,00", 0, 80, 400), at("RABAT -1,00", 0, 100),
        at("6,48 8% 0,52 7,00", 0, 120, 400), at("SUMA: PLN 7,00", 0, 160),
    ]))
    item = result.output.items[0]
    assert (item.before_discount, item.discount, item.total) == (
        Decimal("8.00"), Decimal("-1.00"), Decimal("7.00"))
    assert result.output.total == result.output.amount_due == Decimal("7.00")


def test_sum_label_prefers_matching_font_over_adjacent_tax_amount():
    def large(text, x, y, width):
        return OCRFragment(text, 0.99, ((x,y),(x+width,y),(x+width,y+20),(x,y+20)))
    result = ParseReceiptStep().run(ocr([
        at("PARAGON FISKALNY", 0, 0), at("Produkt 1 x8,00 8,00A", 0, 30, 400),
        at("SUMA PTU", 0, 60), at("1,50", 300, 60),
        large("SUMA PLN", 0, 67, 150), large("8,00", 300, 69, 100),
    ]))
    assert result.output.total == Decimal("8.00")


def test_detached_cross_and_previous_net_do_not_create_another_purchase():
    result = parse(at("Produkt 1 x8,00 8,00A", 0, 45, 400),
                   at("OPUST -2,00", 0, 65), at("6,00A", 300, 85),
                   at("Kolejny produkt", 0, 85), at("×", 200, 85),
                   at("1 x4,00 4,00A", 250, 110), total="10,00")
    assert len(result.output.items) == 2
    assert [item.unit_price for item in result.output.items] == [Decimal("8.00"), Decimal("4.00")]


def test_background_purchase_before_confirmation_does_not_enter_the_table():
    result = ParseReceiptStep().run(ocr([
        at("Obcy 1 x99,00 99,00A", 0, 0, 400),
        at("POTWIERDZENIE TRANSAKCJI", 0, 30, 400), at("NIP 1234567890", 0, 50),
        at("Paliwo 2 L*4,00", 0, 80, 400), at("7,41 8% 0,59 8,00", 0, 110, 400),
        at("SUMA: PLN 8,00", 0, 140),
    ]))
    assert len(result.output.items) == 1
    assert result.output.items[0].unit_price == Decimal("4.00")


def test_section_sharing_purchase_row_does_not_discard_purchase_or_deposit():
    result = ParseReceiptStep().run(ocr([
        at("PARAGON FISKALNY", 0, 0), at("Produkt 1 x4,00 4,00A", 0, 30, 400),
        at("SPRZEDAZ OPODATKOWANA A", 0, 60, 200), at("1 x3,00 3,00A", 300, 60),
        at("SUMA PLN 7,00", 0, 90), at("OPAKOWANIA ZWROTNE WYDANIA", 0, 120, 200),
        at("1 x0,50 0,50", 300, 120), at("DO ZAPLATY 7,50", 0, 160),
    ]))
    assert [item.total for item in result.output.items] == [Decimal("4.00"), Decimal("3.00")]
    assert result.output.deposits[0].total == Decimal("0.50")


def test_completed_purchase_does_not_absorb_later_unrecognized_discount():
    result = parse(at("Produkt 1 x8,00 8,00A", 0, 45, 400),
                   at("OPUST -2,00", 0, 65), at("6,00A", 300, 85),
                   at("Inny produkt bez czytelnej ceny", 0, 100),
                   at("OPUST -1,00", 0, 110), at("2,00A", 300, 125), total="8,00")
    assert result.output.items[0].total == Decimal("6.00")
    assert result.output.items[0].discount == Decimal("-2.00")
    assert "unassigned_discount" in {warning.code for warning in result.warnings}


def test_ambiguous_unsigned_discount_is_not_reported_as_net():
    result = parse(at("Produkt 1?8,00 8,00A", 0, 45, 400),
                   at("�PUST", 0, 65), at("2,00", 300, 65),
                   at("?,??", 300, 85), total="6,00")
    assert result.output.items[0].discount is None
    assert result.output.items[0].total is None


def test_signed_discount_keeps_ownership_despite_skewed_label():
    result = parse(at("OPUST Produkt", 0, 45), at("1 x8,00 8,00A", 250, 45),
                   at("-2,00", 300, 75), at("6,00A", 300, 95), total="6,00")
    assert result.output.items[0].discount == Decimal("-2.00")
    assert result.output.items[0].total == Decimal("6.00")
