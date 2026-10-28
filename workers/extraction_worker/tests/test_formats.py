from io import BytesIO

from PIL import Image
import pytest

from extraction.contracts import PreparationConfig, SourceFile
from extraction.steps.prepare import PreparationError, prepare_document
from test_extraction import pdf_source


def image_source(format="PNG", *, frames=1, size=(32, 48), **kwargs):
    output = BytesIO()
    with Image.new("RGB", size, "red") as image:
        extra = [Image.new("RGB", size, "blue") for _ in range(frames - 1)]
        try:
            image.save(output, format=format, **({"save_all": True, "append_images": extra} if extra else {}), **kwargs)
        finally:
            for frame in extra:
                frame.close()
    return SourceFile(output.getvalue(), "application/octet-stream", "wrong.extension")


@pytest.mark.parametrize("format", ["JPEG", "PNG", "WEBP", "HEIF", "TIFF", "BMP"])
def test_supported_content_is_decoded_despite_filename_and_mime(format):
    source = image_source(format)
    document = prepare_document([source])
    assert document.sources == (source,)
    page, = document.pages
    assert (page.width, page.height) == (32, 48)
    with Image.open(BytesIO(page.content)) as image:
        assert image.mode == "RGB" and image.format == "PNG"


def test_pdf_content_is_detected_despite_metadata():
    pdf = pdf_source(page_count=2)
    source = SourceFile(pdf.content, "image/jpeg", "wrong.jpg")
    assert len(prepare_document([source]).pages) == 2


def test_tiff_preserves_all_pages_order_and_identifiers():
    source = image_source("TIFF", frames=2)
    document = prepare_document([source, image_source()])
    assert [(p.number, p.source_index, p.source_page_number) for p in document.pages] == [(1, 1, 1), (2, 1, 2), (3, 2, 1)]
    with Image.open(BytesIO(document.pages[0].content)) as first, Image.open(BytesIO(document.pages[1].content)) as second:
        assert first.getpixel((0, 0)) == (255, 0, 0)
        assert second.getpixel((0, 0)) == (0, 0, 255)


def test_heif_uses_primary_photo_without_extra_images():
    source = image_source("HEIF", frames=2, primary_index=1)
    page, = prepare_document([source]).pages
    assert page.source_page_number == 1
    with Image.open(BytesIO(page.content)) as image:
        r, g, b = image.getpixel((0, 0))
        assert b > 200 and r < 30


@pytest.mark.parametrize("format", ["PNG", "WEBP"])
def test_animated_images_are_rejected(format):
    with pytest.raises(PreparationError) as error:
        prepare_document([image_source(format, frames=2, duration=100, loop=0)])
    assert error.value.code == "unsupported_format"


def test_alpha_is_composited_on_white():
    output = BytesIO()
    with Image.new("RGBA", (10, 10), (0, 0, 0, 0)) as image:
        image.save(output, format="PNG")
    page, = prepare_document([SourceFile(output.getvalue(), "image/png", "transparent.png")]).pages
    with Image.open(BytesIO(page.content)) as image:
        assert image.getpixel((0, 0)) == (255, 255, 255)


def test_page_limit_is_checked_before_pdf_rendering(monkeypatch):
    import pypdfium2
    monkeypatch.setattr(pypdfium2.PdfPage, "render", lambda *args, **kwargs: pytest.fail("must not render"))
    with pytest.raises(PreparationError) as error:
        prepare_document([pdf_source(page_count=5)], PreparationConfig(max_pages=4))
    assert error.value.code == "too_many_pages"


def test_all_sources_are_preflighted_before_image_decode(monkeypatch):
    import extraction.steps.prepare as module
    monkeypatch.setattr(module, "_page", lambda *args: pytest.fail("must not decode"))
    with pytest.raises(PreparationError) as error:
        prepare_document([image_source(), image_source("TIFF", frames=4)], PreparationConfig(max_pages=4))
    assert error.value.code == "too_many_pages"


def test_pdf_pixel_limit_is_checked_before_rendering(monkeypatch):
    import pypdfium2
    monkeypatch.setattr(pypdfium2.PdfPage, "render", lambda *args, **kwargs: pytest.fail("must not render"))
    with pytest.raises(PreparationError) as error:
        prepare_document([pdf_source(page_count=1)], PreparationConfig(max_page_pixels=10))
    assert error.value.code == "image_too_large"


def test_local_discovery_includes_new_extensions(tmp_path):
    from extraction.runner import collect_jobs, read_sources
    for suffix in (".webp", ".heic", ".heif", ".tif", ".tiff", ".bmp"):
        (tmp_path / ("receipt" + suffix)).write_bytes(image_source().content)
    jobs = collect_jobs(tmp_path)
    assert len(jobs) == 6
    assert all(len(read_sources(files).files) == 1 for _, files in jobs)


@pytest.mark.parametrize("content,code", [(b"unknown", "unsupported_format"),
                                         (b"\x89PNG\r\ninvalid", "invalid_file")])
def test_unrecognized_and_corrupt_content_have_distinct_codes(content, code):
    with pytest.raises(PreparationError) as error:
        prepare_document([SourceFile(content, "image/png", "receipt.png")])
    assert error.value.code == code


@pytest.mark.parametrize("config", [PreparationConfig(max_page_pixels=1535), PreparationConfig(max_document_pixels=3071)])
def test_pixel_limits_are_checked_before_decode(monkeypatch, config):
    import extraction.steps.prepare as module
    monkeypatch.setattr(module, "_page", lambda *args: pytest.fail("must not decode"))
    with pytest.raises(PreparationError) as error:
        prepare_document([image_source(), image_source()], config)
    assert error.value.code == "image_too_large"


def test_exact_limits_accept_four_pages():
    pages = prepare_document([image_source("TIFF", frames=4)], PreparationConfig(
        max_pages=4, max_page_pixels=1536, max_document_pixels=6144)).pages
    assert len(pages) == 4


@pytest.mark.parametrize("field", ["max_pages", "max_page_pixels", "max_document_pixels"])
@pytest.mark.parametrize("value", [0, -1, True, 1.5])
def test_invalid_preparation_limits(field, value):
    with pytest.raises(ValueError):
        PreparationConfig(**{field: value})
