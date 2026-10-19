from types import SimpleNamespace

import pytest

from extraction.reporting import to_dict
from extraction.steps.ocr.common import fragment
from extraction.steps.ocr.paddleocr import PaddleOCRStep
from extraction.steps.prepare import prepare_document
from test_extraction import image_source


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
