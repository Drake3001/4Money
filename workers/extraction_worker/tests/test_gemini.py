import json
from io import BytesIO
from types import SimpleNamespace
from urllib.error import HTTPError

import pytest

from experiments import gemini as experiment
from extraction.steps.parsing import gemini
from extraction.steps.parsing import GeminiParseStep
from extraction.pipeline import Pipeline
from extraction.steps.validate import ValidateReceiptStep
from extraction.reporting import to_dict
from test_parsing import at, document, ocr


def receipt():
    return {
        "store": "Żabka", "date": "2026-10-07", "currency": "PLN",
        "total": "3.95", "amount_due": "3.95",
        "items": [{"name": "Napój", "quantity": "1", "unit": None,
                   "unit_price": "3.95", "before_discount": "3.95", "discount": "0.00", "total": "3.95"}],
        "deposits": [], "deposit_returns": [],
        "evidence": [{"field": "items[0].unit_price", "fragments": ["p1:f2"], "method": "observed"}],
        "issues": [],
    }


def saved_input(tmp_path):
    source = tmp_path / "saved" / "receipt.png"
    source.mkdir(parents=True)
    value = ocr(document(at("Napój 1 x3,95 3,95A", 0, 45)))
    (source / "ocr.json").write_text(json.dumps(to_dict(value)), encoding="utf-8")
    refs = tmp_path / "references"
    refs.mkdir()
    (refs / "receipt.expected.json").write_text(json.dumps({"source": "receipt.png", **receipt()}), encoding="utf-8")
    return source.parent, refs, value


def response(raw=None, finish="STOP"):
    return {"candidates": [{"finishReason": finish,
                            "content": {"parts": [{"text": json.dumps(raw if raw is not None else receipt())}]}}],
            "usageMetadata": {"promptTokenCount": 100, "candidatesTokenCount": 50, "thoughtsTokenCount": 20},
            "modelVersion": "gemini-test-version"}


def test_key_file_and_environment_precedence(tmp_path, monkeypatch):
    env = tmp_path / ".env"
    env.write_text("OTHER=unused\nGEMINI_API_KEY='file-key'\n", encoding="utf-8")
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    assert experiment.load_api_key(env) == "file-key"
    monkeypatch.setenv("GEMINI_API_KEY", "environment-key")
    assert experiment.load_api_key(env) == "environment-key"


def test_missing_key_is_a_local_error(tmp_path, monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    with pytest.raises(ValueError, match="Set GEMINI_API_KEY"):
        experiment.load_api_key(tmp_path / ".env")


def test_request_has_geometry_and_no_filename_or_reference_data():
    value = ocr([at("Napój 1 x3,95 3,95A", 100, 200)])
    payload = gemini.build_request(value)
    page = json.loads(payload["contents"][0]["parts"][0]["text"])[0]
    assert page["fragments"][0]["id"] == "p1:f0"
    assert page["fragments"][0]["box"][0] == [100, 67]
    assert "page-1.png" not in json.dumps(payload)
    assert payload["generationConfig"]["responseJsonSchema"] == gemini.SCHEMA


def test_decode_preserves_decimal_strings_and_original_evidence():
    value = ocr(document(at("Napój 1 x3,95 3,95A", 0, 45)))
    outcome = gemini.decode_receipt(receipt(), value)
    assert str(outcome.output.items[0].unit_price) == "3.95"
    assert outcome.output.evidence["items[0].unit_price"].fragments == ((1, 2),)
    assert "missing_ai_evidence" in {warning.code for warning in outcome.warnings}
    assert "unsupported_ai_value" not in {warning.code for warning in outcome.warnings}


def test_observed_number_absent_from_source_is_flagged():
    value = ocr(document(at("Napój 1 x3,95 3,95A", 0, 45)))
    raw = receipt()
    raw["items"][0]["unit_price"] = "99.99"
    outcome = gemini.decode_receipt(raw, value)
    assert "unsupported_ai_value" in {warning.code for warning in outcome.warnings}


@pytest.mark.parametrize("value", [3.95, "NaN", "Infinity", "3,95", "3.950", "1e3", True])
def test_invalid_money_is_not_silently_coerced(value):
    raw = receipt()
    raw["total"] = value
    with pytest.raises(ValueError, match="decimal string"):
        gemini.decode_receipt(raw, ocr([]))


def test_unknown_evidence_and_invalid_date_are_rejected():
    raw = receipt()
    raw["evidence"][0]["fragments"] = ["p9:f99"]
    with pytest.raises(ValueError, match="evidence"):
        gemini.decode_receipt(raw, ocr([]))
    raw = receipt()
    raw["date"] = "2026-02-30"
    with pytest.raises(ValueError):
        gemini.decode_receipt(raw, ocr([]))


def test_response_cache_avoids_second_api_call_and_does_not_save_key(tmp_path, monkeypatch):
    step = gemini.GeminiParseStep("test-secret-key", "gemini-3.1-flash-lite", tmp_path)
    calls = []
    monkeypatch.setattr(step, "_generate", lambda body: calls.append(body) or response())
    value = ocr(document(at("Napój 1 x3,95 3,95A", 0, 45)))
    step.run(value)
    assert not step.cache_hit
    step.run(value)
    assert step.cache_hit and len(calls) == 1
    assert "test-secret-key" not in json.dumps(step.config)
    assert "test-secret-key" not in next(tmp_path.glob("*.json")).read_text(encoding="utf-8")


def test_truncated_response_is_preserved_but_not_scored_as_success(tmp_path, monkeypatch):
    step = gemini.GeminiParseStep("test-key", "gemini-3.1-flash-lite", tmp_path)
    monkeypatch.setattr(step, "_generate", lambda body: response(finish="MAX_TOKENS"))
    with pytest.raises(ValueError, match="truncated"):
        step.run(ocr([]))
    assert step.response and len(list(tmp_path.glob("*.json"))) == 1


def test_http_error_does_not_expose_provider_body_or_key(tmp_path, monkeypatch):
    def fail(request, **kwargs):
        assert request.get_header("X-goog-api-key") == "test-secret-key"
        assert "test-secret-key" not in request.full_url
        raise HTTPError(request.full_url, 403, "test-secret-key", {}, BytesIO(b"test-secret-key"))

    monkeypatch.setattr(gemini, "build_opener", lambda *args: SimpleNamespace(open=fail))
    step = gemini.GeminiParseStep("test-secret-key", "gemini-3.1-flash-lite", tmp_path)
    with pytest.raises(RuntimeError, match="HTTP 403") as error:
        step._generate(b"{}")
    assert "test-secret-key" not in str(error.value) and step.api_failed


def test_dry_run_never_reads_key_or_calls_api(tmp_path, monkeypatch):
    source, refs, _ = saved_input(tmp_path)
    monkeypatch.setattr(experiment, "load_api_key", lambda: pytest.fail("No key needed in dry run"))
    monkeypatch.setattr(gemini.GeminiParseStep, "_generate", lambda *a: pytest.fail("No API in dry run"))
    root = experiment.run_gemini(source, refs, tmp_path / "output")
    request = json.loads((root / "receipt.png/request.json").read_text(encoding="utf-8"))
    metadata = json.loads((root / "gemini-run.json").read_text(encoding="utf-8"))
    assert request["model"] == "gemini-3.1-flash-lite"
    assert metadata["calls"] == [] and not experiment.run_failed(root)
    assert not (root / "comparison.json").exists()


def test_budget_is_checked_before_key_or_network(tmp_path, monkeypatch):
    source, refs, _ = saved_input(tmp_path)
    monkeypatch.setattr(experiment, "load_api_key", lambda: pytest.fail("Budget should fail first"))
    with pytest.raises(ValueError, match="exceeds budget"):
        experiment.run_gemini(source, refs, tmp_path / "output", execute=True, max_cost_usd="0.000001")
    assert not (tmp_path / "output").exists()


def test_mocked_paid_run_reports_same_cases_cost_usage_and_cached_rerun(tmp_path, monkeypatch):
    source, refs, _ = saved_input(tmp_path)
    monkeypatch.setattr(experiment, "WORKER_DIR", tmp_path)
    monkeypatch.setattr(experiment, "load_api_key", lambda: "test-key")
    monkeypatch.setattr(gemini.GeminiParseStep, "_generate", lambda *args: response())
    root = experiment.run_gemini(source, refs, tmp_path / "output", execute=True)
    report = json.loads((root / "comparison.json").read_text(encoding="utf-8"))
    meta = json.loads((root / "gemini-run.json").read_text(encoding="utf-8"))
    assert report["gemini"]["documents"] == report["rules"]["documents"] == 1
    assert report["gemini"]["fields"]["total"]["match"] == 1
    assert meta["calls"][0]["usage"]["thoughtsTokenCount"] == 20
    assert meta["calls"][0]["estimated_cost_usd"] == "0.00013"
    assert (root / "receipt.png/ocr.json").exists() and not experiment.run_failed(root)
    again = experiment.run_gemini(source, refs, tmp_path / "output", execute=True)
    meta = json.loads((again / "gemini-run.json").read_text(encoding="utf-8"))
    assert meta["calls"][0]["cache_hit"] and meta["calls"][0]["estimated_cost_usd"] == "0"


def test_failed_api_case_counts_as_missing_prediction(tmp_path, monkeypatch):
    source, refs, _ = saved_input(tmp_path)
    monkeypatch.setattr(experiment, "load_api_key", lambda: "test-key")
    monkeypatch.setattr(experiment, "WORKER_DIR", tmp_path)

    def fail(*args):
        raise RuntimeError("Gemini HTTP 429")

    monkeypatch.setattr(gemini.GeminiParseStep, "_generate", fail)
    root = experiment.run_gemini(source, refs, tmp_path / "output", execute=True)
    report = json.loads((root / "comparison.json").read_text(encoding="utf-8"))
    assert report["gemini"]["documents"] == 1 and report["gemini"]["fields"]["total"]["missing"] == 1
    assert report["gemini_cases"][0]["execution_error"] and experiment.run_failed(root)


def test_manifest_selection_is_evenly_spaced_and_rejects_paths(tmp_path):
    manifest = tmp_path / "holdout-manifest.json"
    manifest.write_text(json.dumps({"new_sources": [f"case-{i}" for i in range(9)]}), encoding="utf-8")
    assert experiment._sources(tmp_path, 3) == ["case-0", "case-4", "case-8"]
    manifest.write_text(json.dumps({"new_sources": ["../secret"]}), encoding="utf-8")
    with pytest.raises(ValueError, match="Invalid source"):
        experiment._sources(tmp_path, 10)


def test_invalid_reference_fails_before_key_or_network(tmp_path, monkeypatch):
    source, refs, _ = saved_input(tmp_path)
    (refs / "receipt.expected.json").write_text('{"source": "wrong.png"}', encoding="utf-8")
    monkeypatch.setattr(experiment, "load_api_key", lambda: pytest.fail("Validate references first"))
    with pytest.raises(ValueError, match="Reference source"):
        experiment.run_gemini(source, refs, tmp_path / "output", execute=True)


def test_http_failure_stops_remaining_requests(tmp_path, monkeypatch):
    source, refs, _ = saved_input(tmp_path)
    second = source / "second.png"
    second.mkdir()
    (second / "ocr.json").write_bytes((source / "receipt.png/ocr.json").read_bytes())
    monkeypatch.setattr(experiment, "load_api_key", lambda: "test-key")
    monkeypatch.setattr(experiment, "WORKER_DIR", tmp_path)
    calls = []

    def fail(request, **kwargs):
        calls.append(request)
        raise HTTPError(request.full_url, 429, "quota", {}, BytesIO())

    monkeypatch.setattr(gemini, "build_opener", lambda *args: SimpleNamespace(open=fail))
    root = experiment.run_gemini(source, refs, tmp_path / "output", execute=True)
    metadata = json.loads((root / "gemini-run.json").read_text(encoding="utf-8"))
    assert len(calls) == 1 and metadata["unattempted_sources"] == ["second.png"]
    assert metadata["stop_reason"] and experiment.run_failed(root)


def test_main_selects_saved_holdout_and_defaults_to_dry_run(tmp_path, monkeypatch):
    import main

    saved = tmp_path / "output" / "saved"
    saved.mkdir(parents=True)
    (saved / "holdout-manifest.json").write_text("{}", encoding="utf-8")
    monkeypatch.setattr(main, "INPUT_DIR", tmp_path / "input")
    monkeypatch.setattr(main, "OUTPUT_DIR", saved.parent)
    monkeypatch.setattr(main, "EXPERIMENT", "gemini")
    monkeypatch.setattr(main, "GEMINI_OCR_DIR", None)
    monkeypatch.setattr(main, "GEMINI_EXECUTE", False)
    calls = []
    monkeypatch.setattr(experiment, "run_gemini", lambda *args, **kwargs: calls.append((args, kwargs)) or saved)
    monkeypatch.setattr(experiment, "run_failed", lambda root: False)
    assert main.main() == 0
    assert calls[0][0][0] == saved and calls[0][1]["execute"] is False


def test_gemini_step_runs_in_regular_pipeline_without_experiment_or_cache(monkeypatch):
    step = GeminiParseStep("test-key")
    calls = []
    monkeypatch.setattr(step, "_generate", lambda body: calls.append(body) or response())
    pipeline = Pipeline([("parse", step), ("validate", ValidateReceiptStep())])
    value = ocr(document(at("Napój 1 x3,95 3,95A", 0, 45)))
    for _ in range(2):
        result = pipeline.run(value)
        assert result.ok and str(result.output.items[0].unit_price) == "3.95"
        assert [entry.name for entry in result.steps] == ["parse", "validate"]
    assert step.cache_dir is None and not step.cache_hit and len(calls) == 2
