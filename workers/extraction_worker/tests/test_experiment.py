import sys
from io import BytesIO
from types import SimpleNamespace

import pytest
from extraction.pipeline import Pipeline
from extraction.steps.docscan import DocscanStep
from extraction.steps.prepare import prepare_document
from extraction.steps.ocr import RapidOCRStep
from extraction.steps.uvdoc import UVDocStep
from PIL import Image
from test_extraction import image_source


def test_independent_variants_and_geometry():
    document = prepare_document([image_source()])
    before = document.pages[0].content
    crop = DocscanStep()

    def transform(content):
        output = BytesIO()
        Image.new("RGB", (15, 30)).save(output, format="PNG")
        return output.getvalue()

    crop._transform = transform
    ocr = RapidOCRStep()
    ocr._engine = lambda content: SimpleNamespace(txts=None, scores=None, boxes=None)
    altered = Pipeline([("crop", crop), ("ocr", ocr)]).run(document)
    original = Pipeline([("ocr", ocr)]).run(document)
    assert (altered.output.pages[0].width, altered.output.pages[0].height) == (15, 30)
    assert (original.output.pages[0].width, original.output.pages[0].height) == (40, 20)
    assert document.pages[0].content == before
    assert altered.output.document.sources is document.sources


def test_uvdoc_mutating_library_and_channel_order(monkeypatch):
    np = pytest.importorskip("numpy")
    output = BytesIO()
    Image.new("RGB", (2, 2), (200, 100, 20)).save(output, format="PNG")
    from extraction.contracts import SourceFile

    doc = prepare_document([SourceFile(output.getvalue(), "image/png", "image.png")])
    before = doc.pages[0].content

    class Unwarper:
        def __init__(self, **kwargs):
            pass

        def predict(self, bgr):
            assert bgr[0, 0].tolist() == [20, 100, 200]
            bgr[:] = 0
            return [{"doctr_img": np.full((2, 2, 3), [200, 100, 20], dtype=np.uint8)}]

    monkeypatch.setitem(
        sys.modules, "paddleocr", SimpleNamespace(TextImageUnwarping=Unwarper)
    )
    step = UVDocStep()
    for _ in range(2):
        result = step.run(doc)
        with Image.open(BytesIO(result.output.pages[0].content)) as image:
            assert image.getpixel((0, 0)) == (200, 100, 20)
    assert doc.pages[0].content == before


def test_experiment_prepares_once_and_reuses_steps(tmp_path, monkeypatch):
    from experiments import preprocessing
    from extraction.contracts import OCRResult, PreparedDocument, StepResult
    from extraction.steps.prepare import PrepareDocumentStep
    from extraction.steps.ocr import RapidOCRStep
    from test_main import write_image

    input_dir = tmp_path / "input"
    input_dir.mkdir()
    write_image(input_dir / "first.png")
    write_image(input_dir / "second.png")
    count = {"prepare": 0, "ocr": 0, "docscan": 0, "uvdoc": 0}
    original = PrepareDocumentStep.run

    def prepare(self, input):
        count["prepare"] += 1
        return original(self, input)

    class FakeOCR(RapidOCRStep):
        def run(self, input):
            count["ocr"] += 1
            return StepResult(OCRResult(input, (), "test", "1", {}))

    class Identity:
        input_type = output_type = PreparedDocument
        version = "test"
        config = {}

        def __init__(self, kind):
            self.kind = kind

        def run(self, input):
            count[self.kind] += 1
            return StepResult(input)

    monkeypatch.setattr(PrepareDocumentStep, "run", prepare)
    monkeypatch.setattr(preprocessing, "RapidOCRStep", FakeOCR)
    monkeypatch.setattr(preprocessing, "DocscanStep", lambda: Identity("docscan"))
    monkeypatch.setattr(preprocessing, "UVDocStep", lambda: Identity("uvdoc"))
    root, results = preprocessing.run_preprocessing(input_dir, tmp_path / "output")
    assert count == {"prepare": 2, "ocr": 6, "docscan": 2, "uvdoc": 2}
    assert (root / "comparison.md").exists()
    assert [p.name for p in (root / "original").iterdir() if p.is_dir()] == [
        "first.png",
        "second.png",
    ]
    assert (
        results["original"]["first.png"].output.document
        is results["docscan"]["first.png"].output.document
    )


def test_docscan_reuses_background_model(monkeypatch):
    pytest.importorskip("cv2")
    pytest.importorskip("imutils")
    document = prepare_document([image_source()])
    png = BytesIO()
    Image.new("RGBA", (40, 20), (255, 255, 255, 255)).save(png, format="PNG")
    sessions = []

    def new_session(model):
        session = object()
        sessions.append(session)
        return session

    def remove(content, session, alpha_matting):
        assert session is sessions[0] and alpha_matting is False
        return png.getvalue()

    monkeypatch.setitem(
        sys.modules, "rembg", SimpleNamespace(remove=remove, new_session=new_session)
    )
    monkeypatch.setitem(
        sys.modules,
        "docscan.doc",
        SimpleNamespace(IMG_RESIZE_H=500.0, APPROX_POLY_DP_ACCURACY_RATIO=0.02),
    )
    step = DocscanStep()
    assert step.run(document).output.sources is document.sources
    assert step.run(document).output.sources is document.sources
    assert len(sessions) == 1
