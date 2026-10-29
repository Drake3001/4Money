from dataclasses import replace
from io import BytesIO
import numpy as np
import pytest
from PIL import Image

from extraction.contracts import PreparedDocument, SourceFile
from extraction.pipeline import Pipeline
from extraction.steps.orientation import OrientationStep
from extraction.steps.prepare import prepare_document


def document():
    # An asymmetric image catches wrong rotation direction and clipped pixels.
    image = Image.new("RGB", (7, 11))
    image.putdata([(x * 30, y * 20, (x + y) * 10) for y in range(11) for x in range(7)])
    buffer = BytesIO()
    image.save(buffer, format="PNG")
    return prepare_document([SourceFile(buffer.getvalue(), "image/png", "receipt.png")])


@pytest.mark.parametrize("angle", [0, 90, 180, 270])
def test_quarter_turns_preserve_pixels_and_source_metadata(angle):
    original = document()
    page = original.pages[0]
    with Image.open(BytesIO(page.content)) as upright:
        with upright.rotate(-angle, expand=True) as sideways:
            buffer = BytesIO()
            sideways.save(buffer, format="PNG")
            page = replace(page, content=buffer.getvalue(), width=sideways.width, height=sideways.height)
    input = PreparedDocument(original.sources, (page,))
    step = OrientationStep()
    step._predict = lambda image: (angle, 0.99)
    result = Pipeline([("orient", step)]).run(input)
    assert result.ok and not result.warnings
    corrected = result.output.pages[0]
    with Image.open(BytesIO(corrected.content)) as output:
        with Image.open(BytesIO(original.pages[0].content)) as expected:
            assert output.size == expected.size
            assert output.tobytes() == expected.tobytes()
    assert (corrected.width, corrected.height) == (7, 11)
    assert result.output.sources is original.sources
    assert replace(corrected, content=page.content, width=page.width, height=page.height) == page
    assert input.pages[0] is page
    if angle == 0:
        assert corrected is page and corrected.content is page.content


def test_mixed_pages_and_uncertain_prediction_keep_original_bytes():
    original = document()
    pages = tuple(replace(original.pages[0], number=n) for n in range(1, 4))
    step = OrientationStep(confidence_threshold=0.8)
    predictions = iter([(0, 0.99), (90, 0.79), (270, 0.8)])
    step._predict = lambda image: next(predictions)
    result = step.run(PreparedDocument(original.sources, pages))
    assert result.output.pages[:2] == pages[:2]
    assert all(result.output.pages[i] is pages[i] for i in (0, 1))
    assert (result.output.pages[2].width, result.output.pages[2].height) == (11, 7)
    assert [page.number for page in result.output.pages] == [1, 2, 3]
    assert len(result.warnings) == 1
    assert result.warnings[0].code == "orientation_uncertain"
    assert "Page 2" in result.warnings[0].message


def test_predict_passes_whole_rgb_image_and_handles_negative_angle():
    images = []

    def engine(inputs):
        images.append(inputs[0])
        return [[1], [-90], [0.94]]

    step = OrientationStep()
    step._engine = engine
    with Image.new("RGB", (300, 600), (255, 128, 0)) as image:
        for _ in range(2):
            angle, confidence = step._predict(image)
            assert angle == 270 and confidence == pytest.approx(0.94)
    for rgb in images:
        assert rgb.shape == (600, 300, 3) and rgb.dtype == np.uint8
        assert rgb[0, 0].tolist() == [255, 128, 0]


def test_predictor_initializes_once_with_cpu_and_local_cache(tmp_path, monkeypatch):
    from onnxtr import models

    step = OrientationStep(model_dir=tmp_path)
    loaded = []
    predictor = object()

    def model(**kwargs):
        assert kwargs["cache_dir"] == str(tmp_path)
        config = kwargs["engine_cfg"]
        assert config.providers == ["CPUExecutionProvider"]
        assert config.session_options.intra_op_num_threads == 4
        assert config.session_options.inter_op_num_threads == 1
        loaded.append(object())
        return loaded[-1]

    def build_predictor(model):
        assert model is loaded[0]
        return predictor

    monkeypatch.setattr(models, "mobilenet_v3_small_page_orientation", model)
    monkeypatch.setattr(models, "page_orientation_predictor", build_predictor)
    step.initialize()
    step.initialize()
    assert len(loaded) == 1
    assert step._engine is predictor


@pytest.mark.parametrize("threshold", [-0.1, 1.1, float("nan"), float("inf")])
def test_invalid_confidence_threshold(threshold):
    with pytest.raises(ValueError, match="confidence_threshold"):
        OrientationStep(confidence_threshold=threshold)
