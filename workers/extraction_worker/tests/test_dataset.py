import json
from types import SimpleNamespace
from pathlib import Path

import pytest
from PIL import Image

from dataset import check_dataset, safe_destination, save_json, sync_drive
from extraction.references import validate_reference
from experiments.evaluation import write_evaluation


def reference(source="receipt.jpg"):
    return {
        "source": source, "annotation_method": "manual", "store": "Shop",
        "date": "2026-10-13", "time": "12:00", "currency": "PLN",
        "total": "2.00", "amount_due": "2.00", "uncertain_fields": [],
        "items": [{"name": "Bread", "quantity": "1", "unit": None,
                   "unit_price": "2.00", "before_discount": "2.00",
                   "discount": "0.00", "total": "2.00"}],
    }


def save_reference(path, value):
    path.write_text(json.dumps(value), encoding="utf-8")


class Drive:
    def __init__(self, entries, fail=False):
        self.entries = [SimpleNamespace(id=id, path=path) for id, path in entries]
        self.calls = []
        self.fail = fail

    def download_folder(self, **kwargs):
        assert kwargs["skip_download"] and not kwargs["use_cookies"]
        return self.entries

    def download(self, **kwargs):
        self.calls.append(kwargs["id"])
        if self.fail:
            Path(kwargs["output"]).write_text("<html>error</html>")
        else:
            Image.new("RGB", (2, 2)).save(kwargs["output"])
        return kwargs["output"]


def test_sync_downloads_only_new_media_and_preserves_annotations(tmp_path):
    images = tmp_path / "input"
    images.mkdir()
    existing = images / "old.jpg"
    existing.write_bytes(b"old photo")
    annotation = images / "old.expected.json"
    annotation.write_text("do not replace")
    drive = Drive([("old", "old.jpg"), ("new", "group/01.jpg"), ("json", "old.expected.json")])
    state = tmp_path / "state.json"
    report = sync_drive("link", images, state, drive)
    assert drive.calls == ["new"]
    assert report["downloaded"] == ["group/01.jpg"]
    assert existing.read_bytes() == b"old photo"
    assert annotation.read_text() == "do not replace"
    second = sync_drive("link", images, state, drive)
    assert drive.calls == ["new"] and len(second["existing"]) == 2
    existing.write_bytes(b"changed")
    assert "Local image changed" in sync_drive("link", images, state, drive)["errors"][0]


def test_download_failure_never_becomes_a_completed_photo(tmp_path):
    images = tmp_path / "input"
    images.mkdir()
    drive = Drive([("id", "receipt.jpg")], fail=True)
    assert sync_drive("link", images, tmp_path / "state.json", drive)["errors"]
    assert not (images / "receipt.jpg").exists()
    drive.fail = False
    assert sync_drive("link", images, tmp_path / "state.json", drive)["downloaded"] == ["receipt.jpg"]


def test_duplicate_drive_names_and_changed_ids_are_not_overwritten(tmp_path):
    images = tmp_path / "input"
    images.mkdir()
    drive = Drive([("a", "receipt.jpg"), ("b", "RECEIPT.jpg")])
    state = tmp_path / "state.json"
    assert len(sync_drive("link", images, state, drive)["errors"]) == 2
    assert drive.calls == []
    drive = Drive([("a", "receipt.jpg")])
    sync_drive("link", images, state, drive)
    before = (images / "receipt.jpg").read_bytes()
    drive.entries[0].id = "other"
    assert "different Drive file" in sync_drive("link", images, state, drive)["errors"][0]
    assert (images / "receipt.jpg").read_bytes() == before


@pytest.mark.parametrize("name", ["../escape.jpg", "CON.jpg", "a/b/c.jpg", "bad:name.jpg", "a./01.jpg"])
def test_reject_unsafe_and_unsupported_paths(tmp_path, name):
    with pytest.raises(ValueError):
        safe_destination(tmp_path, name)


def test_check_reports_missing_invalid_orphaned_and_changed_group(tmp_path):
    (tmp_path / "missing.jpg").touch()
    (tmp_path / "invalid.jpg").touch()
    (tmp_path / "invalid.expected.json").write_text("{")
    save_reference(tmp_path / "orphan.expected.json", reference("orphan.jpg"))
    group = tmp_path / "group"
    group.mkdir()
    (group / "01.jpg").touch()
    grouped = {**reference("group"), "sources": ["01.jpg"]}
    save_reference(tmp_path / "group.expected.json", grouped)
    report = check_dataset(tmp_path)
    assert report["valid"] == ["group"]
    assert report["missing"][0]["source"] == "missing.jpg"
    assert report["invalid"][0]["source"] == "invalid.jpg"
    assert len(report["orphaned"]) == 1
    (group / "02.jpg").touch()
    assert any(case["source"] == "group" for case in check_dataset(tmp_path)["invalid"])


def test_schema_reports_precise_nested_errors_without_crashing():
    value = reference()
    value["items"][0]["quantity"] = 1
    value["items"][0]["total"] = "NaN"
    value["uncertain_fields"] = [None]
    value["deposits"] = [{"name": "Deposit", "quantity": "1", "unit_price": 0.5, "total": "0.50"}]
    errors = validate_reference(value)
    assert any("items[0].quantity" in error for error in errors)
    assert any("items[0].total" in error for error in errors)
    assert any("uncertain_fields[0]" in error for error in errors)
    assert any("deposits[0].unit_price" in error for error in errors)
    assert validate_reference(reference()) == []


def test_evaluation_accepts_group_and_skips_stale_page_list(tmp_path):
    inputs, run = tmp_path / "input", tmp_path / "run"
    (inputs / "group").mkdir(parents=True)
    (inputs / "group/01.jpg").touch()
    value = {**reference("group"), "sources": ["01.jpg"]}
    save_reference(inputs / "group.expected.json", value)
    (run / "group").mkdir(parents=True)
    raw = {"kind": "ocr", "engine": "test", "pages": [{"page_number": 1, "lines": []}]}
    save_reference(run / "group/result.json", {"output": raw})
    output = write_evaluation(run, inputs)
    assert len(json.loads(output.with_suffix(".json").read_text())["cases"]) == 1
    (inputs / "group/02.jpg").touch()
    other_run = tmp_path / "other"
    other_run.mkdir()
    output = write_evaluation(other_run, inputs)
    report = json.loads(output.with_suffix(".json").read_text())
    assert not report["cases"] and "sources" in report["skipped"][0]["reason"]


def test_deposits_can_omit_unprinted_values_and_returns_keep_negative_sign():
    value = reference()
    value["deposits"] = [{"name": "Deposit", "quantity": None, "unit_price": None, "total": "0.50"}]
    value["deposit_returns"] = [{"name": "Return", "quantity": "1", "unit_price": "-1.00", "total": "-1.00"}]
    assert validate_reference(value) == []
    value["deposit_returns"][0]["total"] = "1.00"
    assert any("deposit_returns[0].total" in error for error in validate_reference(value))
    del value["deposits"][0]["quantity"]
    assert any("deposits[0].quantity" in error for error in validate_reference(value))


def test_state_write_retries_temporary_windows_file_lock(tmp_path, monkeypatch):
    target = tmp_path / "state.json"
    save_json(target, {"old": True})
    replace = Path.replace
    calls = []

    def temporarily_locked(path, destination):
        calls.append(path)
        if len(calls) <= 2:
            raise PermissionError("temporary file lock")
        return replace(path, destination)

    monkeypatch.setattr(Path, "replace", temporarily_locked)
    monkeypatch.setattr("dataset.time.sleep", lambda seconds: None)
    save_json(target, {"new": True})
    assert len(calls) == 3
    assert json.loads(target.read_text()) == {"new": True}
