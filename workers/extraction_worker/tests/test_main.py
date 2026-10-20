import json
from types import SimpleNamespace

import pytest
from extraction.contracts import OCRResult, ReceiptData, StepResult
from extraction.pipeline import Pipeline
from extraction.reporting import write_result
from extraction.runner import collect_jobs, run_batch, run_case, read_sources
from extraction.steps.demo import DemoStep
from extraction.steps.prepare import PrepareDocumentStep
from extraction.steps.ocr import RapidOCRStep
from test_rapidocr import Boxes
from PIL import Image


def write_image(path):
    Image.new("RGB", (40, 20), "white").save(path)


def test_grouping_continue_debug_and_no_overwrite(tmp_path):
    input = tmp_path / "input"
    input.mkdir()
    write_image(input / "loose.png")
    (input / "bad.png").write_bytes(b"broken")
    folder = input / "receipt"
    folder.mkdir()
    write_image(folder / "02.png")
    write_image(folder / "01.png")
    (folder / "nested").mkdir()
    write_image(folder / "nested" / "ignored.png")
    jobs = dict(collect_jobs(input))
    assert [p.name for p in jobs["receipt"]] == ["01.png", "02.png"]
    pipeline = Pipeline([("prepare", PrepareDocumentStep()), ("demo", DemoStep())])
    root, results = run_batch(pipeline, input, tmp_path / "out", True)
    assert not results["bad.png"].ok and results["receipt"].ok
    assert len(results["receipt"].captured_steps["prepare"].pages) == 2
    assert (root / "receipt/debug/prepare/002.png").exists()
    before = (root / "receipt/result.json").read_bytes()
    with pytest.raises(FileExistsError):
        write_result(root / "receipt", results["receipt"])
    root2, _ = run_batch(pipeline, input, tmp_path / "out")
    assert root2 != root and not (root2 / "receipt/debug").exists()
    assert (root / "receipt/result.json").read_bytes() == before
    assert (root / "summary.md").exists()


def test_parsed_run_retains_ocr_without_debug_images(tmp_path):
    source = tmp_path / "receipt.png"
    write_image(source)
    ocr = RapidOCRStep()
    ocr._engine = lambda content: SimpleNamespace(
        txts=("ŻABKA",), scores=(0.99,), boxes=Boxes()
    )

    class Parse:
        input_type = OCRResult
        output_type = ReceiptData
        version = "test"
        config = {}

        def run(self, input):
            return StepResult(ReceiptData(store=input.pages[0].lines[0].text))

    pipeline = Pipeline([
        ("prepare", PrepareDocumentStep()), ("recognize", ocr), ("parse", Parse())
    ])
    folder = tmp_path / "parsed"
    result = run_case(pipeline, read_sources((source,)), folder)
    assert result.ok and result.output.store == "ŻABKA"
    assert set(result.captured_steps) == {"recognize"}
    assert "ocr_only" not in {warning.code for warning in result.warnings}
    assert sorted(path.name for path in folder.iterdir()) == [
        "ocr.json", "ocr.txt", "result.json"
    ]
    payload = json.loads((folder / "result.json").read_text(encoding="utf-8"))
    assert payload["output"]["store"] == "ŻABKA" and "captured_steps" not in payload
    raw = json.loads((folder / "ocr.json").read_text(encoding="utf-8"))
    assert raw["kind"] == "ocr" and raw["pages"][0]["lines"][0]["text"] == "ŻABKA"
    assert "content" not in raw["document"]["pages"][0]
    assert "ŻABKA" in (folder / "ocr.txt").read_text(encoding="utf-8")

    # Explicit captures may still be retained without writing page images.
    captured = pipeline.run(read_sources((source,)), capture_steps=True)
    write_result(tmp_path / "no_images", captured, save_images=False)
    assert not (tmp_path / "no_images/debug").exists()


def test_runner_marks_only_final_ocr_output_as_unparsed(tmp_path):
    source = tmp_path / "receipt.png"
    write_image(source)
    ocr = RapidOCRStep()
    ocr._engine = lambda content: SimpleNamespace(txts=None, scores=None, boxes=None)
    pipeline = Pipeline([("prepare", PrepareDocumentStep()), ("ocr", ocr)])
    result = run_case(pipeline, read_sources((source,)), tmp_path / "ocr_only")
    assert [warning.code for warning in result.warnings].count("ocr_only") == 1
    assert not (tmp_path / "ocr_only/ocr.json").exists()
