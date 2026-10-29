import copy
from io import BytesIO

import pytest
from PIL import Image

from experiments.image_latency import COMPACT_OUTPUT, encode_image, study_request
from extraction.contracts import OCRResult, PreparedDocument, SourceFile


def png(size):
    output = BytesIO()
    Image.new("RGB", size, "white").save(output, format="PNG")
    return output.getvalue()


def test_png_control_preserves_bytes_exactly():
    original = png((31, 17))
    content, mime, metrics = encode_image(original, "png")
    assert content == original
    assert mime == "image/png" and metrics["size"] == (31, 17)


def test_jpeg_changes_encoding_without_resizing_or_orientation():
    original = png((310, 170))
    content, mime, metrics = encode_image(original, "jpeg95")
    with Image.open(BytesIO(content)) as image:
        assert image.format == "JPEG" and image.size == (310, 170)
        assert image.getexif().get(274) is None
    assert mime == "image/jpeg" and metrics["size"] == metrics["original_size"]
    assert encode_image(original, "jpeg95_compact_output")[0] == content


def test_resizing_bounds_long_edge_without_upscaling():
    content, _, metrics = encode_image(png((3024, 4032)), "jpeg95_2048")
    with Image.open(BytesIO(content)) as image:
        assert image.size == (1536, 2048)
    assert metrics["original_size"] == (3024, 4032)
    assert encode_image(png((100, 200)), "jpeg95_2048")[2]["size"] == (100, 200)


def test_output_format_instruction_does_not_change_images_or_ocr():
    from extraction.contracts import OCRPage
    images = [SourceFile(png((10, 20)), "image/png", "private.png")]
    ocr = OCRResult(PreparedDocument((), ()), (OCRPage(1, "private.png", 1, 1, 10, 20, ()),), "test", "1", {})
    baseline = study_request(images, ocr, "jpeg95")
    compact = study_request(images, ocr, "jpeg95_compact_output")
    assert compact["messages"][0]["content"] == baseline["messages"][0]["content"] + COMPACT_OUTPUT
    compact["messages"][0] = copy.deepcopy(baseline["messages"][0])
    assert compact == baseline
    assert baseline["provider"]["only"] == ["friendli"]
    assert baseline["provider"]["allow_fallbacks"] is False
    assert "sort" not in baseline["provider"]


def test_unknown_encoding_fails_before_processing():
    with pytest.raises(ValueError):
        encode_image(b"invalid", "unknown")
