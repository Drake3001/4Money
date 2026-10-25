import copy
import json

import pytest

from experiments.gemma_batch import evaluate_review, run_batch
from extraction.steps.enrich import BASE_URL, FREE_MODEL, OpenRouterEnricher, PaidOpenRouterEnricher, ProviderHTTPError
from test_parsing import at, document, ocr


@pytest.fixture
def cases(monkeypatch):
    monkeypatch.setattr("experiments.gemma_batch.load_config", lambda: {
        "api_key": "test-key", "model": FREE_MODEL, "base_url": BASE_URL})
    return [(f"case-{i}.png", ocr(document(at(f"Produkt{i} 1 x3,00 3,00A", 0, 50), total="3,00")),
             "fixture") for i in range(3)]


def fake_response(body):
    payload = json.loads(body)
    items = json.loads(payload["messages"][1]["content"])["items"]
    return {"choices": [{"finish_reason": "stop", "message": {"content": json.dumps({"items": [
        {"id": item["id"], "name_corrected": item["name_raw"], "category": "inne", "needs_review": False, "review_reason": None}
        for item in items]})}}]}


def test_dry_run_saves_plan_and_unreviewed_labels_without_api(cases, tmp_path):
    root, report = run_batch(cases=cases, output_dir=tmp_path)
    assert report["api_calls"] == 0
    assert len(report["cases"]) == 3
    assert all(c["status"] == "prepared" for c in report["cases"])
    assert report["accuracy"]["category_accuracy"] is None
    assert (root / "review.template.json").is_file()
    for path in root.glob("*/request.json"):
        assert "accepted_names" not in path.read_text()
        assert "test-key" not in path.read_text()


def test_at_most_one_request_per_case_and_cache_costs_no_calls(cases, tmp_path, monkeypatch):
    client = OpenRouterEnricher("test-key", cache_dir=tmp_path / "cache")
    monkeypatch.setattr(client, "_generate", fake_response)
    _, report = run_batch(cases=cases, output_dir=tmp_path, execute=True, client=client)
    assert report["api_calls"] == 3
    assert all(c["status"] == "completed" for c in report["cases"])
    _, cached = run_batch(cases=cases, output_dir=tmp_path, execute=True, client=client)
    assert cached["api_calls"] == 0
    assert all(c["cache_hit"] for c in cached["cases"])


def test_first_error_stops_remaining_requests(cases, tmp_path, monkeypatch):
    client = OpenRouterEnricher("test-key")
    def fail(body):
        raise RuntimeError("OpenRouter HTTP 429")
    monkeypatch.setattr(client, "_generate", fail)
    _, report = run_batch(cases=cases, output_dir=tmp_path, execute=True, client=client)
    assert report["api_calls"] == 1
    assert [c["status"] for c in report["cases"]] == ["failed", "skipped_after_error", "skipped_after_error"]
    assert report["accuracy"]["name_accuracy_after"] is None


@pytest.mark.parametrize("limit", [0, 1, 6, 100, True])
def test_reject_out_of_range_limits(limit, tmp_path):
    with pytest.raises(ValueError):
        run_batch(limit=limit, output_dir=tmp_path)
    assert not list(tmp_path.iterdir())


def test_review_counts_improvements_regressions_and_category_accuracy():
    cases = [{"source": "a", "status": "completed", "parser": {"items": [
        {"name": "M1eko"}, {"name": "Woda"}, {"name": "Nieoceniony"}]},
        "enrichment": {"items": [
            {"name_corrected": "Mleko", "category": "żywność i napoje"},
            {"name_corrected": "Wódka", "category": "alkohol i tytoń"},
            {"name_corrected": "Zmieniony", "category": "inne"}]}}]
    review = {"cases": [{"source": "a", "items": [
        {"id": 0, "reviewed": True, "accepted_names": ["MLEKO"], "category": "żywność i napoje"},
        {"id": 1, "reviewed": True, "accepted_names": ["Woda"], "category": "żywność i napoje"},
        {"id": 2, "reviewed": False, "accepted_names": ["Nieoceniony"], "category": "inne"}]}]}
    score = evaluate_review(cases, review)
    assert score["names_scored"] == 2
    assert score["name_accuracy_before"] == score["name_accuracy_after"] == 0.5
    assert score["category_accuracy"] == 0.5
    assert score["names_improved"] == score["names_regressed"] == 1
    bad = copy.deepcopy(review)
    bad["cases"][0]["items"].append(bad["cases"][0]["items"][0])
    with pytest.raises(ValueError, match="duplicate"):
        evaluate_review(cases, bad)
    cases[0]["status"] = "failed"
    assert evaluate_review(cases, review)["names_scored"] == 0


def test_retry_server_error_once_and_count_against_total_budget(cases, tmp_path, monkeypatch):
    client = OpenRouterEnricher("test-key")
    calls = []
    waits = []
    def generate(body):
        calls.append(body)
        if len(calls) == 1:
            raise ProviderHTTPError("Google Gemma", 500, "server error")
        return fake_response(body)
    monkeypatch.setattr(client, "_generate", generate)
    monkeypatch.setattr("experiments.gemma_batch.sleep", waits.append)
    _, report = run_batch(cases=cases, output_dir=tmp_path, execute=True, client=client,
                         max_calls=3, retry_server_error=True, min_interval=20)
    assert report["api_calls"] == len(calls) == 3
    assert [c["status"] for c in report["cases"]] == ["completed", "completed", "failed"]
    assert report["cases"][0]["attempts"][0]["http_status"] == 500
    assert len(waits) == 2 and all(19 < wait <= 20 for wait in waits)


@pytest.mark.parametrize("status,expected_calls", [(500, 2), (429, 1), (400, 1), (403, 1)])
def test_retry_is_bounded_and_only_for_server_errors(cases, tmp_path, monkeypatch, status, expected_calls):
    client = OpenRouterEnricher("test-key")
    def fail(body):
        raise ProviderHTTPError("Google Gemma", status, "safe failure")
    monkeypatch.setattr(client, "_generate", fail)
    _, report = run_batch(cases=cases, output_dir=tmp_path, execute=True, client=client,
                         retry_server_error=True, max_calls=3)
    assert report["api_calls"] == expected_calls
    assert [c["status"] for c in report["cases"]] == ["failed", "skipped_after_error", "skipped_after_error"]


def test_no_retry_when_attempt_cap_already_reached(cases, tmp_path, monkeypatch):
    client = OpenRouterEnricher("test-key")
    def fail(body):
        raise ProviderHTTPError("Google Gemma", 500, "server error")
    monkeypatch.setattr(client, "_generate", fail)
    _, report = run_batch(cases=cases, output_dir=tmp_path, execute=True, client=client,
                         retry_server_error=True, max_calls=1)
    assert report["api_calls"] == 1


def test_paid_mode_reserves_budget_and_tracks_new_cost_not_cached_cost(cases, tmp_path, monkeypatch):
    client = PaidOpenRouterEnricher("test-key", cache_dir=tmp_path / "cache")
    def generate(body):
        payload = json.loads(body)
        assert payload["model"] == "google/gemma-4-31b-it"
        assert payload["max_tokens"] == 1024
        assert payload["provider"]["max_price"] == {"prompt": 0.30, "completion": 0.60}
        response = fake_response(body)
        response["usage"] = {"cost": 0.0001}
        return response
    monkeypatch.setattr(client, "_generate", generate)
    root, report = run_batch(cases=cases, output_dir=tmp_path, execute=True, client=client, paid=True)
    assert report["api_calls"] == 3
    assert report["reported_cost_usd"] == "0.0003"
    assert 0 < float(report["reserved_cost_usd"]) < 0.10
    assert len(list(root.glob("*/response.json"))) == 3
    _, cached = run_batch(cases=cases, output_dir=tmp_path, execute=True, client=client, paid=True)
    assert cached["api_calls"] == 0
    assert cached["reported_cost_usd"] == cached["reserved_cost_usd"] == "0"


def test_paid_preflight_blocks_tiny_budget_without_requests(cases, tmp_path, monkeypatch):
    client = PaidOpenRouterEnricher("test-key")
    def forbidden(body):
        pytest.fail("Budget must be checked before any request")
    monkeypatch.setattr(client, "_generate", forbidden)
    with pytest.raises(ValueError, match="preflight"):
        run_batch(cases=cases, output_dir=tmp_path, execute=True, client=client, paid=True,
                  max_cost_usd="0.000001")
    with pytest.raises(ValueError, match="explicit paid"):
        run_batch(cases=cases, output_dir=tmp_path, execute=True, client=client)


@pytest.mark.parametrize("budget", ["NaN", "Infinity", "0", "-1", "1"])
def test_invalid_paid_budget_is_rejected(cases, tmp_path, budget):
    with pytest.raises(ValueError):
        run_batch(cases=cases, output_dir=tmp_path, paid=True, max_cost_usd=budget)
