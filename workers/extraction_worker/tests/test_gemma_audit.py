import json

import pytest

from experiments.gemma_audit import audit_saved_run
from experiments.gemma_batch import evaluate_review


def test_offline_audit_maps_labels_without_fixing_wrong_classifications(tmp_path, monkeypatch):
    def forbidden(*args, **kwargs):
        pytest.fail("Offline audit must never access credentials or network")
    monkeypatch.setattr("urllib.request.OpenerDirector.open", forbidden)
    monkeypatch.setattr("experiments.gemma.load_config", forbidden)
    run = tmp_path / "old"
    run.mkdir()
    original = {"limit": 3, "api_calls": 3, "cases": [{
        "source": "receipt.jpg", "status": "completed", "parser": {"items": [
            {"name": "Mleko"}, {"name": "Ręcznik papierowy"}]},
        "enrichment": {"items": [
            {"id": 0, "name_corrected": "Mleko", "category": "żywność"},
            {"id": 1, "name_corrected": "Ręcznik papierowy", "category": "higiena i kosmetyki"}]}}]}
    report_file = run / "report.json"
    report_file.write_text(json.dumps(original), encoding="utf-8")
    before = report_file.read_bytes()
    review = {"taxonomy_version": "1", "reviewer": "test", "cases": [{"source": "receipt.jpg", "items": [
        {"id": 0, "name_raw": "Mleko", "reviewed": True, "accepted_names": ["Mleko"], "category": "żywność i napoje"},
        {"id": 1, "name_raw": "Ręcznik papierowy", "reviewed": True,
         "accepted_names": ["Ręcznik papierowy"], "category": "dom i ogród"}]}]}
    labels = tmp_path / "review.json"
    labels.write_text(json.dumps(review), encoding="utf-8")
    root, result = audit_saved_run(run, labels, tmp_path / "audits")
    assert result["accuracy"]["category_accuracy"] == 0.5
    assert result["accuracy"]["name_accuracy_after"] == 1
    assert result["accuracy"]["auto_category_accuracy"] is None
    assert result["api_calls"] == 0
    assert result["cases"][0]["enrichment"]["items"][1]["category"] == "kosmetyki i higiena"
    assert (root / "audit.md").is_file()
    assert report_file.read_bytes() == before
    assert result["taxonomy_version"] == "1"
    assert result["source_taxonomy_version"] is None
    review["cases"][0]["items"][0]["name_raw"] = "Different frozen input"
    with pytest.raises(ValueError, match="frozen"):
        evaluate_review(result["cases"], review)


def test_reviewing_only_confident_items_does_not_hide_abstentions():
    cases = [{"source": "a", "status": "completed", "parser": {"items": [
        {"name": "Kawa"}, {"name": "Mleko"}]}, "enrichment": {"items": [
            {"name_corrected": "Kawa", "category": None, "needs_review": True},
            {"name_corrected": "Mleko", "category": "żywność i napoje", "needs_review": False}]}}]
    review = {"cases": [{"source": "a", "items": [
        {"id": i, "reviewed": True, "category": "żywność i napoje", "accepted_names": None}
        for i in range(2)]}]}
    metrics = evaluate_review(cases, review)
    assert metrics["category_accuracy"] == 0.5
    assert metrics["auto_category_accuracy"] == 1
    assert metrics["auto_categories_scored"] == 1
    assert metrics["review_requested"] == 1


def test_historical_review_keeps_v1_categories_and_rejects_new_discount_label():
    cases = [{"source": "a", "status": "prepared", "parser": {"items": [{"name": "Rabat"}]}}]
    review = {"taxonomy_version": "1", "cases": [{"source": "a", "items": [{
        "id": 0, "reviewed": True, "category": "inne", "accepted_names": None}]}]}
    assert evaluate_review(cases, review)["categories_scored"] == 0
    review["cases"][0]["items"][0]["category"] = "zniżka"
    with pytest.raises(ValueError, match="Unknown expected category"):
        evaluate_review(cases, review)
    review["taxonomy_version"] = "2"
    assert evaluate_review(cases, review)["categories_scored"] == 0
