import json
from types import SimpleNamespace

from extraction.contracts import OCRResult, SourceFiles
from extraction.pipeline import Pipeline
from extraction.reporting import result_to_json, write_result
from extraction.steps.prepare import PrepareDocumentStep
from extraction.steps.rapidocr import RapidOCRStep
from test_extraction import image_source


class Boxes:
    def tolist(self):
        return [[[1, 2], [30, 2], [30, 10], [1, 10]]]


def test_structured_ocr_provenance_and_single_serialization(tmp_path):
    ocr = RapidOCRStep()
    ocr._engine = lambda content: SimpleNamespace(
        txts=("ŻABKA",), scores=(0.75,), boxes=Boxes()
    )
    result = Pipeline([("prepare", PrepareDocumentStep()), ("ocr", ocr)]).run(
        SourceFiles((image_source("one.png"), image_source("two.png")))
    )
    assert result.ok and isinstance(result.output, OCRResult)
    assert [p.page_number for p in result.output.pages] == [1, 2]
    page = result.output.pages[1]
    assert page.source_name == "two.png" and (page.width, page.height) == (40, 20)
    assert page.lines[0].box == ((1.0, 2.0), (30.0, 2.0), (30.0, 10.0), (1.0, 10.0))
    assert len([w for w in result.warnings if w.code == "low_confidence"]) == 2
    payload = json.loads(result_to_json(result))
    assert payload["output"]["pages"][0]["lines"][0]["text"] == "ŻABKA"
    assert "content" not in payload["output"]["document"]["pages"][0]
    assert "raw_response" not in payload
    assert "raw_pages" not in payload["output"]
    assert payload["output"]["pages"][0]["lines"][0]["box"] == [[1, 2], [30, 2], [30, 10], [1, 10]]
    assert result_to_json(result).count('"ŻABKA"') == 2
    write_result(tmp_path / "case", result)
    assert sorted(p.name for p in (tmp_path / "case").iterdir()) == [
        "ocr.txt",
        "result.json",
    ]
    assert "ŻABKA" in (tmp_path / "case/ocr.txt").read_text(encoding="utf-8")


def test_empty_and_failed_ocr():
    ocr = RapidOCRStep()
    ocr._engine = lambda content: SimpleNamespace(txts=None, scores=None, boxes=None)
    pipeline = Pipeline([("prepare", PrepareDocumentStep()), ("ocr", ocr)])
    result = pipeline.run(SourceFiles((image_source(),)))
    assert result.ok and result.output.pages[0].lines == ()
    assert "no_text" in {w.code for w in result.warnings}

    def fail(content):
        raise RuntimeError("inference failed")

    ocr._engine = fail
    failed = pipeline.run(SourceFiles((image_source(),)))
    assert failed.error.step == "ocr" and failed.output.pages[0].width == 40
