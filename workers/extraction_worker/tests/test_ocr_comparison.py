from types import SimpleNamespace

import pytest

from extraction.reporting import to_dict
from extraction.steps.ocr.common import fragment
from extraction.steps.ocr.paddleocr import PaddleOCRStep
from extraction.steps.prepare import prepare_document
from test_extraction import image_source
from extraction.steps.ocr.easyocr import EasyOCRStep
from extraction.steps.ocr.tesseract import TesseractStep
from experiments.evaluation import audit_receipt
from experiments.ocr_metrics import evaluate_cer, normalize_transcription, reconstruct_rows, transcription_text
from test_evaluation import line, raw, reference


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


def full_reference():
    data = reference()
    data.update(annotation_method="manual", store="Shop", currency="PLN",
                transcription_scope="full_image", review_status="reviewed")
    data["items"][0]["unit"] = "szt."
    return data


def test_reading_order_words_and_full_rows_have_same_audit():
    words = [line("3,95C", 10, 310), line("MLECZ.", 10, 105), line("NAPÓJ", 10, 0),
             line("F", 10, 205), line("1 x3,95", 10, 220)]
    row_view = reconstruct_rows(raw(words))
    assert transcription_text(row_view) == "NAPÓJ MLECZ. F 1 x3,95 3,95C"
    assert len(row_view["pages"][0]["lines"][0]["members"]) == 5
    word_audit = audit_receipt(reference(), row_view)
    row_audit = audit_receipt(reference(), reconstruct_rows(raw([line(transcription_text(row_view), 10)])))
    assert word_audit["name_counts"] == row_audit["name_counts"] == {"match": 1}
    assert word_audit["numeric_field_counts"] == row_audit["numeric_field_counts"]
    data = reference()
    data["items"].append(dict(data["items"][0]))
    repeated = raw(words + [{**value, "box": [[x, y+30] for x,y in value["box"]]} for value in words])
    assert audit_receipt(data, reconstruct_rows(repeated))["name_counts"] == {"match": 2}


def test_skewed_reading_order_and_page_boundaries():
    import math
    c, s = math.cos(0.1), math.sin(0.1)
    values = [line("RIGHT", 0, 120), line("LEFT", 0, 0), line("NEXT", 30, 0)]
    for value in values:
        value["box"] = [[x*c-y*s, x*s+y*c] for x,y in value["box"]]
    view = reconstruct_rows(raw(values))
    assert transcription_text(view) == "LEFT RIGHT\nNEXT"
    view = reconstruct_rows({"pages": [{"page_number": 1, "lines": values}, {"page_number": 2, "lines": [line("PAGE2",0)]}]})
    assert transcription_text(view).endswith("NEXT\nPAGE2")


def test_name_prefix_does_not_require_a_readable_line_total():
    audit = audit_receipt(reference(), raw([line("NAPÓJ MLECZ. F 1 x3,95", 0)]))
    assert audit["name_counts"] == {"match": 1}


@pytest.mark.parametrize("observed", ["ZABKA 1,00 -2", "ŻABKA 100 -2", "ŻABKA 1.00 -2", "ŻABKA 1,00 2", "ŻABKA 1,00 -2 dodatkowy tekst"])
def test_cer_preserves_important_characters(observed):
    result = evaluate_cer({"transcription_scope": "full_image"}, "ŻABKA 1,00 -2", observed)
    assert result["cer"] > 0


def test_cer_eligibility_normalization_empty_ocr_and_unbounded_value():
    assert normalize_transcription("  z\u0307\n  1,00 ") == "ż 1,00"
    assert evaluate_cer({}, "abc", "abc")["status"] == "not_evaluated"
    assert evaluate_cer({"transcription_scope": "partial"}, "abc", "abc")["status"] == "not_evaluated"
    assert evaluate_cer({"transcription_scope": "full_image"}, " ", "abc")["status"] == "not_evaluated"
    assert evaluate_cer({"transcription_scope": "full_image"}, "abc", "")["cer"] == 1
    assert evaluate_cer({"transcription_scope": "full_image"}, "a", "abcdef")["cer"] == 5


def test_reference_scope_validation():
    from extraction.references import validate_reference

    value = full_reference()
    assert not validate_reference(value)
    value["transcription_scope"] = "unverified-full"
    assert any("transcription_scope" in error for error in validate_reference(value))


def test_unprinted_total_is_excluded_even_without_discount():
    data = reference()
    data["items"][0]["total_is_printed"] = False
    result = audit_receipt(data, reconstruct_rows(raw([line("NAPÓJ MLECZ. F 1 x3,95 3,95C",10)])))
    assert result["items"][0]["fields"]["total"]["status"] == "not_printed"
    assert result["numeric_field_counts"] == {"match": 3}
