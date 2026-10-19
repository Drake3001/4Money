from types import SimpleNamespace

import pytest

from extraction.reporting import to_dict
from extraction.steps.ocr.common import fragment
from extraction.steps.ocr.paddleocr import PaddleOCRStep
from extraction.steps.prepare import prepare_document
from test_extraction import image_source
from extraction.steps.ocr.easyocr import EasyOCRStep
from extraction.steps.ocr.tesseract import TesseractStep


def test_native_adapter_conversion_and_empty_results(monkeypatch):
    pytest.importorskip("numpy")
    doc = prepare_document([image_source("one.png"), image_source("two.png")])
    paddle = PaddleOCRStep()
    paddle._engine = SimpleNamespace(predict=lambda image: [{"rec_texts": ["ŻÓŁĆ"], "rec_scores": [0.75],
        "rec_polys": [[[1,2],[20,2],[20,10],[1,10]]], "output_img": b"must not serialize"}])
    result = paddle.run(doc)
    output = result.output
    assert output.pages[0].lines[0].text == "ŻÓŁĆ"
    assert [page.source_name for page in output.pages] == ["one.png", "two.png"]
    assert [page.page_number for page in output.pages] == [1, 2]
    assert "raw_pages" not in to_dict(output)
    assert sum(warning.code == "low_confidence" for warning in result.warnings) == 2
    easy = EasyOCRStep()
    easy._engine = SimpleNamespace(readtext=lambda *args, **kwargs: [([[1,2],[20,2],[20,10],[1,10]], "ŻÓŁĆ", 1.1)])
    output = easy.run(doc).output
    assert output.pages[0].lines[0].confidence == 1
    easy._engine.readtext = lambda *args, **kwargs: []
    assert easy.run(doc).warnings[-1].code == "no_text"
    assert doc.pages[0].content == prepare_document([image_source()]).pages[0].content
    with pytest.raises(ValueError):
        fragment("x", float("nan"), [[0,0]]*4)
    with pytest.raises(TypeError):
        fragment(None, 0.9, [[0,0]]*4)


def test_paddle_reuses_engine_without_mutating_configuration(monkeypatch):
    import sys

    options = []
    class Engine:
        _merged_paddlex_config = {"defaults": {"enabled": True}}
    def create(**kwargs):
        options.append(kwargs)
        return Engine()
    monkeypatch.setitem(sys.modules, "paddleocr", SimpleNamespace(PaddleOCR=create))
    step = PaddleOCRStep()
    config = dict(step.config)
    step.initialize()
    first = step._engine
    step.initialize()
    assert step._engine is first and len(options) == 1
    assert step.config == config
    assert options[0]["engine"] == "paddle_static" and options[0]["enable_mkldnn"] is False


def test_tesseract_converts_words_confidence_and_boxes(monkeypatch):
    step = TesseractStep()
    step._initialized = True
    tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n"
    tsv += "4\t1\t1\t1\t1\t0\t0\t0\t30\t10\t-1\t\n"
    tsv += "5\t1\t1\t1\t1\t1\t1\t2\t20\t10\t82.5\tŻÓŁĆ\n"
    monkeypatch.setattr(step, "_call", lambda *args: tsv)
    output = step.run(prepare_document([image_source()])).output
    assert output.pages[0].lines[0].confidence == 0.825
    assert output.pages[0].lines[0].box == ((1.,2.),(21.,2.),(21.,12.),(1.,12.))
    assert len(output.pages[0].lines) == 1
    monkeypatch.setattr(step, "_call", lambda *args: "")
    with pytest.raises(ValueError, match="TSV header"):
        step.run(prepare_document([image_source()]))


def test_tesseract_initialization_and_thread_limit(tmp_path, monkeypatch):
    import subprocess
    from extraction.steps.ocr import tesseract

    for language in ("pol", "eng"):
        (tmp_path / (language + ".traineddata")).write_bytes(b"model")
    monkeypatch.setattr(tesseract.shutil, "which", lambda command: "tesseract.exe")
    calls = []
    def run(args, **kwargs):
        calls.append((args, kwargs))
        text = "tesseract v5.5.3" if "--version" in args else "List of available languages (2):\npol\neng\n"
        return SimpleNamespace(returncode=0, stdout=text.encode(), stderr=b"")
    monkeypatch.setattr(subprocess, "run", run)
    step = TesseractStep(tessdata_dir=tmp_path)
    config = dict(step.config)
    step.initialize()
    step.initialize()
    assert len(calls) == 2
    assert calls[0][1]["env"]["OMP_THREAD_LIMIT"] == "4"
    assert step.version == "tesseract v5.5.3"
    assert step.config == config
    monkeypatch.setattr(tesseract.shutil, "which", lambda command: None)
    with pytest.raises(RuntimeError, match="missing"):
        TesseractStep().initialize()


def test_tesseract_uses_identical_png_without_a_blocking_stdin_pipe(monkeypatch):
    from pathlib import Path

    step = TesseractStep()
    step._initialized = True
    document = prepare_document([image_source()])
    paths = []
    def call(arguments):
        path = Path(arguments[0])
        paths.append(path)
        assert path.read_bytes() == document.pages[0].content
        assert arguments[1] == "stdout"
        return "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n"
    monkeypatch.setattr(step, "_call", call)
    assert not step.run(document).output.pages[0].lines
    assert paths and not paths[0].exists()
