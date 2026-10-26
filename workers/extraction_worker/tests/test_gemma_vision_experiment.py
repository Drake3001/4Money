import json

import pytest

from experiments import gemma_vision
from extraction.providers import OpenRouterClient
from extraction.reporting import to_dict
from extraction.analysis.receipt_images import ReceiptImageAnalyzer
from test_analysis_workflows import envelope
from test_gemma_vision import response, source


@pytest.fixture
def saved_study(tmp_path, monkeypatch):
    image = tmp_path / "case.jpg"
    image.write_bytes(source().content)
    raw = response()
    # Use a food category to match the fixed study's default case category.
    raw["items"][0]["category"] = "żywność i napoje"
    cache = tmp_path / "cache"
    client = ReceiptImageAnalyzer(
        OpenRouterClient("test-key"),
        allow_paid=True,
        cache_dir=cache,
        request_policy=lambda body, send: envelope(raw),
    )
    parsed = client.run([source()]).output
    baseline = tmp_path / "custom-baseline"
    for split in ("development", "holdout"):
        folder = baseline / f"{split}-baseline"
        folder.mkdir(parents=True)
        case = {"source": "case.jpg", "status": "completed", "parser": to_dict(parsed),
                "enrichment": client.enrichment, "chunks": [{"usage": {"cost": "0.0001"}}]}
        (folder / "report.json").write_text(json.dumps({"cases": [case]}), encoding="utf-8")
    manifest = tmp_path / "manifest.json"
    manifest.write_text(json.dumps({"cases": [{"source": "case.jpg", "image_paths": ["case.jpg"]}]}), encoding="utf-8")
    refs = tmp_path / "refs"
    refs.mkdir()
    (refs / "case.expected.json").write_text(json.dumps({"source": "case.jpg", **to_dict(parsed)}), encoding="utf-8")
    monkeypatch.setattr(gemma_vision, "WORKER_DIR", tmp_path)
    monkeypatch.setattr(gemma_vision, "SOURCES", ("case.jpg",))
    monkeypatch.setattr(gemma_vision, "load_config", lambda: pytest.fail("Offline mode must not load credentials"))
    monkeypatch.setattr(ReceiptImageAnalyzer, "_generate", lambda *args: pytest.fail("Network must not be called"))
    return ["--baseline-run", str(baseline), "--manifest", str(manifest), "--references", str(refs),
            "--cache-dir", str(cache), "--output-dir", str(tmp_path / "out"), "--limit", "1"]


def test_explicit_custom_baseline_replays_through_pipeline_without_credentials(saved_study):
    assert gemma_vision.main([*saved_study, "--replay-cache"]) == 0
    from pathlib import Path
    output = Path(saved_study[saved_study.index("--output-dir") + 1])
    report = json.loads(next(output.glob("*/comparison.json")).read_text(encoding="utf-8"))
    assert report["api_calls"] == 0 and report["cases"][0]["cache_hit"]
    assert report["cases"][0]["analysis"]["annotations"][0]["category"] == "żywność i napoje"
    assert [s["name"] for s in report["cases"][0]["steps"]] == ["analyze", "validate"]


def test_missing_offline_cache_fails_without_transport(saved_study, tmp_path):
    saved_study[saved_study.index("--cache-dir") + 1] = str(tmp_path / "absent-cache")
    assert gemma_vision.main([*saved_study, "--replay-cache"]) == 1
