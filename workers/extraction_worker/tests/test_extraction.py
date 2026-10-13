import json
from decimal import Decimal
from io import BytesIO

import pytest
from extraction.contracts import (
    PipelineResult,
    PreparationConfig,
    ReceiptData,
    ReceiptItem,
    SourceFile,
    SourceFiles,
)
from extraction.pipeline import Pipeline
from extraction.reporting import result_to_json
from extraction.steps.demo import DemoStep
from extraction.steps.prepare import PrepareDocumentStep, prepare_document
from extraction.steps.validate import ValidateReceiptStep, validate_receipt
from PIL import Image
from pypdf import PdfWriter


def image_source(name="receipt.png", *, jpeg=False, orientation=None):
    output = BytesIO()
    with Image.new("RGB", (40, 20), "white") as image:
        options = {}
        if orientation is not None:
            exif = Image.Exif()
            exif[274] = orientation
            options["exif"] = exif
        image.save(output, format="JPEG" if jpeg else "PNG", **options)
    return SourceFile(output.getvalue(), "image/jpeg" if jpeg else "image/png", name)


def pdf_source(*, page_count=2, password=None):
    writer = PdfWriter()
    for _ in range(page_count):
        writer.add_blank_page(width=72, height=144)
    if password is not None:
        writer.encrypt(password)
    output = BytesIO()
    writer.write(output)
    return SourceFile(output.getvalue(), "application/pdf", "receipt.pdf")


@pytest.mark.parametrize("jpeg", [False, True])
def test_image_is_rgb_png(jpeg):
    source = image_source(jpeg=jpeg)
    document = prepare_document([source])
    assert document.sources == (source,)
    page = document.pages[0]
    assert (page.number, page.source_page_number, page.source_index) == (1, 1, 1)
    with Image.open(BytesIO(page.content)) as image:
        assert (image.format, image.mode, image.size) == ("PNG", "RGB", (40, 20))


def test_exif_orientation():
    document = prepare_document([image_source(jpeg=True, orientation=6)])
    assert (document.pages[0].width, document.pages[0].height) == (20, 40)
    with Image.open(BytesIO(document.pages[0].content)) as image:
        assert image.getexif().get(274) is None


@pytest.mark.parametrize("dpi", [72, 200])
def test_pdf_rendering(dpi):
    source = pdf_source()
    document = prepare_document([source], PreparationConfig(pdf_dpi=dpi))
    assert document.sources == (source,)
    assert [(p.width, p.height) for p in document.pages] == [(dpi, dpi * 2)] * 2
    assert [p.source_page_number for p in document.pages] == [1, 2]
    assert PreparationConfig().pdf_dpi == 200


@pytest.mark.parametrize("dpi", [0, -10, float("inf"), float("nan")])
def test_invalid_dpi(dpi):
    with pytest.raises(ValueError):
        PreparationConfig(pdf_dpi=dpi)


@pytest.mark.parametrize(
    "total,mismatch", [("3.00", False), ("3.01", False), ("3.02", True)]
)
def test_sum_tolerance(total, mismatch):
    data = ReceiptData(
        total=Decimal(total),
        items=[ReceiptItem(total=Decimal("1")), ReceiptItem(total=Decimal("2"))],
    )
    assert ("total_mismatch" in {w.code for w in validate_receipt(data)}) == mismatch
    assert data.total == Decimal(total)


def test_unknown_amount_skips_comparison():
    data = ReceiptData(
        total=Decimal("100"), items=[ReceiptItem(total=Decimal("1")), ReceiptItem()]
    )
    assert "total_mismatch" not in {w.code for w in validate_receipt(data)}


def test_nonfinite_number_warns():
    data = ReceiptData(total=Decimal("NaN"), items=[ReceiptItem(total=Decimal("2"))])
    assert "invalid_number" in {w.code for w in validate_receipt(data)}


@pytest.mark.parametrize(
    "sources,code",
    [
        ([SourceFile(b"", "image/png", "empty.png")], "empty_file"),
        ([SourceFile(b"broken", "image/png", "broken.png")], "invalid_file"),
        ([SourceFile(b"broken", "application/pdf", "broken.pdf")], "invalid_file"),
        ([SourceFile(b"x", "text/plain", "text")], "unsupported_type"),
    ],
)
def test_invalid_sources(sources, code):
    with pytest.raises(ValueError) as exc:
        prepare_document(sources)
    assert exc.value.code == code
    result = Pipeline([("prepare", PrepareDocumentStep()), ("demo", DemoStep())]).run(
        SourceFiles(sources)
    )
    assert result.output is None and result.error.step == "prepare"


@pytest.mark.parametrize(
    "source,code",
    [
        (pdf_source(password="secret"), "password_required"),
        (pdf_source(page_count=0), "empty_document"),
    ],
)
def test_pdf_errors(source, code):
    with pytest.raises(ValueError) as exc:
        prepare_document([source])
    assert exc.value.code == code


def test_empty_input_and_mime_mismatch():
    with pytest.raises(ValueError):
        SourceFiles(())
    with pytest.raises(ValueError):
        prepare_document([])
    source = image_source()
    with pytest.raises(ValueError):
        prepare_document([SourceFile(source.content, "image/jpeg", source.name)])


def test_mixed_document_and_atomic_failure():
    sources = (image_source("same.png"), pdf_source(), image_source("same.png"))
    document = prepare_document(sources)
    assert document.sources == sources
    assert [
        (p.number, p.source_index, p.source_page_number) for p in document.pages
    ] == [(1, 1, 1), (2, 2, 1), (3, 2, 2), (4, 3, 1)]
    result = Pipeline([("prepare", PrepareDocumentStep())]).run(
        SourceFiles((sources[0], SourceFile(b"bad", "image/png", "bad.png")))
    )
    assert result.output is None


def test_demo_validation_serialization_and_partial_data():
    result = Pipeline(
        [
            ("prepare", PrepareDocumentStep()),
            ("demo", DemoStep()),
            ("validate", ValidateReceiptStep()),
        ]
    ).run(SourceFiles((image_source(),)))
    assert result.ok and result.output.store == "DEMO"
    assert result.warnings[0].code == "demo_result"
    payload = json.loads(result_to_json(result))
    assert (
        payload["output"]["total"] == "12.50"
        and payload["output"]["date"] == "2026-01-01"
    )
    data = ReceiptData(
        total=Decimal("2.00"),
        items=[ReceiptItem(name="Produkt", total=Decimal("2.00"))],
    )
    outcome = ValidateReceiptStep().run(data)
    assert outcome.output is data and data.currency is None
    assert {w.field for w in outcome.warnings} == {"store", "date", "currency"}
    assert (
        json.loads(result_to_json(PipelineResult(output=data)))["output"]["currency"]
        is None
    )
    assert "empty_items" in {
        w.code for w in ValidateReceiptStep().run(ReceiptData()).warnings
    }
