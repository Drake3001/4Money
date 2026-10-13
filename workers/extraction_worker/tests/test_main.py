import pytest
from extraction.pipeline import Pipeline
from extraction.reporting import write_result
from extraction.runner import collect_jobs, run_batch
from extraction.steps.demo import DemoStep
from extraction.steps.prepare import PrepareDocumentStep
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
