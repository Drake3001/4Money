import math
from decimal import Decimal

import pytest

from extraction.contracts import OCRFragment, OCRPage, OCRResult, PreparedDocument
from extraction.steps.parsing import ParseReceiptStep


def at(text, x, y, width=100):
    return OCRFragment(text, 0.99, ((x, y), (x + width, y),
                                    (x + width, y + 10), (x, y + 10)))


def ocr(*pages):
    return OCRResult(PreparedDocument((), ()), tuple(
        OCRPage(i, f"page-{i}.png", i, 1, 1000, 3000, tuple(lines))
        for i, lines in enumerate(pages, 1)
    ), "test", "1", {})


def document(*lines, total="3,95"):
    bottom = max((line.box[0][1] for line in lines), default=30) + 30
    return [at('Sklep "ŻABKA"', 0, 0), at("PARAGON FISKALNY", 0, 20),
            *lines, at("SUMA PLN", 0, bottom), at(total, 300, bottom),
            at("ROZLICZENIE PŁATNOŚCI", 0, bottom + 20),
            at("2026-10-07 15:19", 0, bottom + 40)]


def parse(*lines, total="3,95"):
    return ParseReceiptStep().run(ocr(document(*lines, total=total)))


@pytest.mark.parametrize("symbol", ["x", "×", "*"])
@pytest.mark.parametrize("separator", [",", "."])
def test_purchase_symbols_and_decimal_separators(symbol, separator):
    amount = f"3{separator}95"
    outcome = parse(at("Napój mleczny", 0, 50), at(f"1szt. {symbol}{amount} {amount}A", 200, 50))
    item = outcome.output.items[0]
    assert len(outcome.output.items) == 1
    assert (item.name, item.quantity, item.unit) == ("Napój mleczny", Decimal(1), "szt")
    assert item.unit_price == item.before_discount == item.total == Decimal("3.95")
    assert item.discount == 0
    assert outcome.output.store == "Żabka"
    assert outcome.output.amount_due == Decimal("3.95")
    assert outcome.output.evidence["amount_due"].method == "derived"


def test_weighted_quantity_and_cent_rounding():
    result = parse(at("Jabłka C", 0, 50), at("1,298kg *16,99 22,05 C", 200, 50), total="22,05")
    item = result.output.items[0]
    assert (item.name, item.quantity, item.unit, item.total) == (
        "Jabłka", Decimal("1.298"), "kg", Decimal("22.05"))


def test_compact_multiplication_with_separate_total_and_tax_marker():
    result = parse(at("Napój", 0, 45), at("2x3,95", 200, 45),
                   at("7,90", 320, 45), at("C", 440, 45), total="7,90")
    assert result.output.items[0].quantity == 2
    assert result.output.items[0].total == Decimal("7.90")


def test_split_fragments_rotated_scaled_and_shuffled_keep_ownership():
    lines = document(at("Napój", 0, 50), at("1 x3,95", 200, 50), at("3,95C", 320, 50),
                     at("Bułka", 0, 80), at("2 x1,00", 200, 80), at("2,00C", 320, 80), total="5,95")
    c, s = math.cos(0.12), math.sin(0.12)
    transformed = [OCRFragment(line.text, line.confidence, tuple(
        (2 * (x*c-y*s), 2 * (x*s+y*c)) for x, y in line.box
    )) for line in reversed(lines)]
    result = ParseReceiptStep().run(ocr(transformed))
    assert [item.name for item in result.output.items] == ["Napój", "Bułka"]
    assert [item.total for item in result.output.items] == [Decimal("3.95"), Decimal("2.00")]
    refs = result.output.evidence["items[0].total"].fragments
    assert any(transformed[index].text == "3,95C" for _, index in refs)


def test_multiline_name_and_numeric_next_line():
    result = parse(at("Napój mleczny", 0, 45), at("waniliowy 500ml", 0, 58),
                   at("1 x3,95 3,95A", 200, 71))
    assert result.output.items[0].name == "Napój mleczny waniliowy 500ml"


def test_missing_names_and_repeated_products_do_not_borrow_previous_name():
    result = parse(at("Bułka C", 0, 45), at("1SZT *1,00 1,00 C", 200, 45),
                   at("1SZT *1,00 1,00 C", 200, 70),
                   at("Bułka C", 0, 95), at("1SZT *1,00 1,00 C", 200, 95), total="3,00")
    assert [item.name for item in result.output.items] == ["Bułka", None, "Bułka"]
    assert "missing_item_name" in {warning.code for warning in result.warnings}


@pytest.mark.parametrize("printed", [False, True])
def test_item_discount_and_summary_are_not_double_counted(printed):
    lines = [at("Napój", 0, 45), at("2 x2,99 5,98C", 200, 45),
             at("OPUST Napój", 0, 65), at("-2,00C", 300, 65)]
    if printed:
        lines.append(at("3,98C", 300, 85))
    lines.append(at("OPUSTY ŁĄCZNIE -2,00", 0, 105))
    result = parse(*lines, total="3,98")
    item = result.output.items[0]
    assert (item.before_discount, item.discount, item.total) == (
        Decimal("5.98"), Decimal("-2.00"), Decimal("3.98"))
    assert result.output.evidence["items[0].total"].method == ("observed" if printed else "derived")


def test_quantity_on_discount_line_is_not_another_product():
    result = parse(at("Napój 1 x2,00 2,00A", 0, 45, 400),
                   at("OPUST 1 x0,50 -0,50A", 0, 65, 400), total="1,50")
    assert len(result.output.items) == 1
    assert result.output.items[0].discount == Decimal("-0.50")


def test_unreadable_discount_is_unknown_and_does_not_become_zero():
    result = parse(at("Napój 1 x3,95 3,95A", 0, 45, 400), at("OPUST -O,50", 0, 65))
    assert result.output.items[0].discount is None
    assert result.output.items[0].total is None
    assert "missing_discount" in {warning.code for warning in result.warnings}


def test_final_amount_can_supply_missing_discount_without_repairing_digits():
    result = parse(at("Napój 1 x3,95 3,95A", 0, 45, 400),
                   at("OPUST -O,50", 0, 65), at("3,45A", 300, 85), total="3,45")
    assert result.output.items[0].discount == Decimal("-0.50")
    assert result.output.evidence["items[0].discount"].method == "derived"


def test_conflicting_same_line_totals_are_not_resolved_by_arithmetic():
    result = parse(at("Napój 1 x3,95 3,95A 9,99B", 0, 45, 400))
    assert result.output.items[0].before_discount is None
    assert result.output.items[0].total is None
    assert "ambiguous_field" in {warning.code for warning in result.warnings}


def test_higher_standalone_amount_does_not_create_positive_discount():
    result = parse(at("Napój 1 x3,95 3,95A", 0, 45, 400), at("9,99", 300, 65))
    assert result.output.items[0].discount is None
    assert "ambiguous_field" in {warning.code for warning in result.warnings}


@pytest.mark.parametrize("due_lines", [
    [at("DO ZAPŁATY 3,95 4,95", 0, 100)],
    [at("DO ZAPŁATY ???", 0, 100)],
])
def test_explicit_unresolved_amount_due_is_not_replaced_by_goods_total(due_lines):
    result = ParseReceiptStep().run(ocr([
        at("PARAGON FISKALNY", 0, 20), at("Napój 1 x3,95 3,95A", 0, 45),
        at("SUMA PLN 3,95", 0, 80), *due_lines,
    ]))
    assert result.output.total == Decimal("3.95")
    assert result.output.amount_due is None
    assert any(warning.field == "amount_due" for warning in result.warnings)


def test_unrelated_distant_amount_is_not_assigned_to_total_label():
    result = ParseReceiptStep().run(ocr([
        at("PARAGON FISKALNY", 0, 20), at("Napój 1 x3,95 3,95A", 0, 45),
        at("SUMA PLN", 0, 80), at("99,99", 800, 600),
    ]))
    assert result.output.total is None


def test_letter_x_in_product_name_is_not_a_multiplication_sign():
    result = parse(at("Box 3,95", 0, 45))
    assert result.output.items == []


def test_unreadable_printed_amount_can_be_derived_and_is_marked():
    result = parse(at("Napój 1 x3,95 3,958", 0, 45, 400))
    assert result.output.items[0].total == Decimal("3.95")
    assert result.output.evidence["items[0].before_discount"].method == "derived"
    assert result.output.evidence["items[0].total"].method == "derived"


def test_invalid_quantity_is_not_partially_read_as_a_different_number():
    result = parse(at("Papryka", 0, 45), at("G,224 x14,99 3,36C", 200, 45), total="3,36")
    assert result.output.items[0].quantity is None
    assert result.output.items[0].name == "Papryka"
    assert result.output.items[0].before_discount == Decimal("3.36")


def test_deposits_returns_and_goods_totals_are_separate():
    result = ParseReceiptStep().run(ocr([
        at("ALDI", 0, 0), at("PARAGON FISKALNY", 0, 20),
        at("Napój 1 *44,89 44,89A", 0, 45, 400), at("SUMA PLN 44,89", 0, 70),
        at("OPAKOWANIA ZWROTNE WYDANIA", 0, 90),
        at("Opak. zwrot. 2 *0,50 1,00", 0, 110, 400),
        at("OPAKOWANIA ZWROTNE PRZYJĘCIA", 0, 130),
        at("Zwrot opak. 1 *-28,00 -28,00", 0, 150, 400),
        at("OPAKOWANIA ZUROTNE SUMA -27,00", 0, 170),
        at("DO ZAPŁATY 17,89 PLN", 0, 190),
    ]))
    data = result.output
    assert len(data.items) == len(data.deposits) == len(data.deposit_returns) == 1
    assert data.total == Decimal("44.89") and data.amount_due == Decimal("17.89")
    assert data.deposit_returns[0].total == Decimal("-28.00")


def test_deposit_without_quantity_and_unfinished_label():
    lines = [at("PARAGON FISKALNY", 0, 20), at("SUMA PLN 0,00", 0, 40),
             at("OPAKOWANIA ZWROTNE WYDANIA", 0, 60), at("KAUCJA ZA PUSZKĘ", 0, 80),
             at("0.50", 300, 80), at("KAUCJA ZA BUTELKĘ", 0, 100)]
    result = ParseReceiptStep().run(ocr(lines))
    assert len(result.output.deposits) == 1
    assert result.output.deposits[0].quantity is result.output.deposits[0].unit_price is None
    assert result.output.amount_due is None
    assert "unparsed_deposit" in {warning.code for warning in result.warnings}


def test_tax_payment_background_and_second_receipt_do_not_add_items():
    lines = [at("Obcy 1 x99,00 99,00A", 0, 0), at("PARAGON FISKALNY", 0, 20),
             at("Napój 1 x3,95 3,95A", 0, 45), at("SPRZEDAŻ OPODATKOWANA A 3,95", 0, 70),
             at("SUMA PTU 0,74", 0, 90), at("SUMA PLN 3,95", 0, 110),
             at("ROZLICZENIE PŁATNOŚCI", 0, 130), at("KARTA 3,95 PLN", 0, 150),
             at("19.10.2026 12:00", 0, 170), at("PARAGON FISKALNY", 0, 200),
             at("Obcy 1 x99,00 99,00A", 0, 220), at("SUMA PLN 99,00", 0, 240)]
    result = ParseReceiptStep().run(ocr(lines))
    assert len(result.output.items) == 1
    assert result.output.total == Decimal("3.95")
    assert result.output.date.isoformat() == "2026-10-19"


def test_pages_keep_item_order_and_original_evidence_indices():
    page1 = [at("ŻABKA", 0, 0), at("PARAGON FISKALNY", 0, 20), at("Bułka 1 x1,00 1,00A", 0, 45)]
    page2 = [at("ŻABKA", 0, 0), at("PARAGON FISKALNY", 0, 20),
             at("Napój 1 x2,00 2,00A", 0, 45), at("SUMA PLN 3,00", 0, 80)]
    result = ParseReceiptStep().run(ocr(page1, page2))
    assert [item.total for item in result.output.items] == [Decimal(1), Decimal(2)]
    assert result.output.total == Decimal(3)
    assert result.output.evidence["items[1].name"].fragments == ((2, 2),)


def test_discount_continuation_is_reported_instead_of_silently_discarded():
    page1 = [at("PARAGON FISKALNY", 0, 20), at("Bułka 1 x2,00 2,00A", 0, 45)]
    page2 = [at("OPUST -0,50", 0, 10), at("SUMA PLN 1,50", 0, 40)]
    result = ParseReceiptStep().run(ocr(page1, page2))
    assert "unassigned_discount" in {warning.code for warning in result.warnings}


def test_distant_footer_negative_amount_is_not_a_product_discount():
    result = ParseReceiptStep().run(ocr([at("PARAGON FISKALNY", 0, 20),
        at("Bułka 1 x2,00 2,00A", 0, 45), at("INNA KOREKTA -20,00", 0, 1000)]))
    assert result.output.items[0].discount == 0


@pytest.mark.parametrize("lines", [[], [at("Hello 1 x2,00 2,00A", 0, 10)]])
def test_empty_or_unrecognized_document_returns_partial_receipt(lines):
    result = ParseReceiptStep().run(ocr(lines))
    assert result.output.items == []
    assert result.output.total is None
    assert "missing_receipt_section" in {warning.code for warning in result.warnings}
