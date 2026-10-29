import copy
from dataclasses import replace

import pytest

from experiments.gemma_hard import partial_checks, single_page_ocr
from extraction.contracts import DocumentPage, PreparedDocument, SourceFiles
from extraction.steps.analysis import analyzed_receipt
from extraction.analysis.receipt_images import decode_vision
from test_gemma_vision import response, source
from test_parsing import at, document, ocr


def test_partial_input_never_includes_other_capture_text():
    value = ocr(document(at("Visible first page", 0, 50)))
    image = source()
    page = replace(value.pages[0], source_name=image.name)
    second = replace(page, page_number=2, source_name="hidden.jpg", lines=(replace(page.lines[0], text="HIDDEN TOTAL 999.00"),))
    docpage = DocumentPage(b"", 1, image.name, 1, 1, page.width, page.height)
    other = replace(docpage, number=2, source_name="hidden.jpg", source_index=2)
    value = replace(value, pages=(page, second), document=PreparedDocument((image, image), (docpage, other)))
    sliced = single_page_ocr(value, 1, SourceFiles((image,)))
    assert len(sliced.pages) == len(sliced.document.pages) == 1
    assert sliced.pages[0].text == page.text
    assert "Visible first page" in sliced.pages[0].text
    assert "HIDDEN" not in sliced.pages[0].text
    with pytest.raises(ValueError, match="another source"):
        single_page_ocr(value, 2, SourceFiles((image,)))


def test_partial_control_distinguishes_unseen_fills_from_unknown_groups():
    raw = response()
    decoded, enriched, evidence = decode_vision(raw, 1)
    analysis = analyzed_receipt(decoded.output, enriched, evidence)
    reference = {"source": "partial", **copy.deepcopy(raw),
                 "expected_abstentions": ["date", "total", "deposit_returns", "time", "items[0].quantity"]}
    reference["date"] = reference["total"] = None
    reference["items"][0]["quantity"] = None
    result = {c["field"]: c for c in partial_checks(reference, analysis)}
    assert result["date"]["status"] == result["total"]["status"] == "filled_unseen_field"
    assert result["items[0].quantity"]["status"] == "filled_unseen_field"
    assert result["time"]["status"] == "not_in_output_contract"
    assert result["deposit_returns"]["status"] == "unknown_not_distinguished_from_empty_group"
