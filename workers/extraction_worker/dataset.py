"""Download public Drive receipts and report work for a human/visual agent."""

import argparse
import hashlib
import json
import re
import tempfile
import time
from pathlib import Path

from PIL import Image

from extraction.references import validate_reference
from extraction.runner import MIME_TYPES, collect_jobs

WORKER = Path(__file__).resolve().parent
DEFAULT_DRIVE = "https://drive.google.com/drive/folders/1b8EdHHBsqE_3J9MUKn_lFupfwt7YW9pA"


def save_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    # OneDrive/antivirus can briefly hold the destination on Windows.
    for attempt in range(6):
        try:
            temp.replace(path)
            break
        except PermissionError:
            if attempt == 5:
                raise
            time.sleep(0.05 * 2**attempt)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def safe_destination(root, relative):
    """Reject unsupported nesting and names that collide on Windows."""
    path = Path(relative)
    parts = path.parts
    if path.is_absolute() or path.drive or not 1 <= len(parts) <= 2:
        raise ValueError(f"Unsupported receipt path: {relative!r}; only one folder level is supported")
    for part in parts:
        if part in (".", "..") or part.endswith((" ", ".")) or re.search(r'[<>:"/\\|?*\x00-\x1f]', part):
            raise ValueError(f"Unsafe filename: {relative!r}")
        if re.fullmatch(r"(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])", part.split(".")[0]):
            raise ValueError(f"Reserved Windows filename: {relative!r}")
    destination = root / path
    if not destination.resolve().is_relative_to(root.resolve()):
        raise ValueError(f"Path escapes input directory: {relative!r}")
    return destination


def verify_media(path, suffix):
    if suffix == ".pdf":
        if not path.read_bytes().startswith(b"%PDF-"):
            raise ValueError("Downloaded content is not a PDF")
    else:
        with Image.open(path) as image:
            expected = "PNG" if suffix == ".png" else "JPEG"
            if image.format not in ({"JPEG", "MPO"} if expected == "JPEG" else {"PNG"}):
                raise ValueError(f"Expected {expected}, got {image.format}")
            image.verify()


def sync_drive(url, input_dir, state_path, client=None):
    if client is None:
        try:
            import gdown as client
        except ImportError as exc:
            raise RuntimeError('Install dataset dependencies: uv pip install --python .venv/Scripts/python.exe -e ".[dataset]"') from exc
    state = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else {}
    if not isinstance(state, dict):
        raise ValueError("Invalid sync state: expected an object")
    entries = client.download_folder(url=url, output=str(input_dir), skip_download=True,
                                     quiet=True, use_cookies=False, timeout=30)
    if entries is None:
        raise RuntimeError("Could not list the Drive folder")
    if not entries:
        raise RuntimeError("Drive returned an empty listing; check the link and folder contents")
    report = {"downloaded": [], "existing": [], "ignored": [], "errors": []}
    candidates = []
    counts = {}
    for entry in entries:
        if Path(entry.path).suffix.lower() not in MIME_TYPES:
            report["ignored"].append(entry.path)
            continue
        try:
            target = safe_destination(input_dir, entry.path)
            key = str(target.relative_to(input_dir)).replace("\\", "/")
            local_path = state.get(key, {}).get("local_path")
            if local_path:
                target = safe_destination(input_dir, local_path)
                if target.suffix.lower() != Path(entry.path).suffix.lower():
                    raise ValueError(f"Mapped destination changes media type: {local_path!r}")
        except ValueError as exc:
            report["errors"].append(str(exc))
            continue
        destination_key = str(target).casefold()
        counts[destination_key] = counts.get(destination_key, 0) + 1
        candidates.append((entry, target, key))
    for entry, target, key in candidates:
        try:
            if counts[str(target).casefold()] > 1:
                raise ValueError("Drive contains duplicate destination names")
            old = state.get(key)
            if old and old["id"] != entry.id:
                raise ValueError("A different Drive file occupies this name; existing data preserved")
            if target.exists():
                if not target.is_file() or target.stat().st_size == 0:
                    raise ValueError("Existing destination is not a nonempty file")
                sha = digest(target)
                if old and sha != old["sha256"]:
                    raise ValueError("Local image changed since sync; review its annotation")
                report["existing"].append(key)
                # Adopt pre-existing local photos without replacing or downloading them.
                state[key] = {**(old or {}), "id": entry.id, "sha256": sha}
                print(f"Existing: {key}", flush=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.TemporaryDirectory(prefix=".download-", dir=input_dir.parent) as temporary:
                partial = Path(temporary) / target.name
                result = client.download(id=entry.id, output=str(partial), quiet=True,
                                         use_cookies=False, timeout=30, retries=2)
                if result is None or not partial.is_file() or partial.stat().st_size == 0:
                    raise ValueError("Download did not produce a complete file")
                verify_media(partial, target.suffix.lower())
                sha = digest(partial)
                if old and sha != old["sha256"]:
                    raise ValueError("Drive content changed; old reference requires review")
                # Exclusive creation protects existing data, including concurrent runs.
                with target.open("xb") as output:
                    try:
                        with partial.open("rb") as source:
                            import shutil
                            shutil.copyfileobj(source, output)
                    except BaseException:
                        output.close()
                        target.unlink()
                        raise
            state[key] = {**(old or {}), "id": entry.id, "sha256": sha}
            report["downloaded"].append(key)
            print(f"Downloaded: {key}", flush=True)
        except Exception as exc:
            report["errors"].append(f"{key}: {exc}")
        finally:
            save_json(state_path, state)
    return report


def check_dataset(input_dir):
    report = {"missing": [], "invalid": [], "valid": [], "orphaned": [],
              "missing_text": [], "layout_errors": [], "pending_review": []}
    jobs = collect_jobs(input_dir)
    references = {}
    for name, files in jobs:
        grouped = (input_dir / name).is_dir()
        stem = name if grouped else Path(name).stem
        references.setdefault(stem.casefold(), []).append(name)
        expected = input_dir / f"{stem}.expected.json"
        case = {"source": name, "images": [str(p.resolve()) for p in files],
                "reference": str(expected.resolve())}
        if not (input_dir / f"{stem}.expected.txt").is_file():
            report["missing_text"].append(name)
        if not expected.exists():
            report["missing"].append(case)
            continue
        try:
            value = json.loads(expected.read_text(encoding="utf-8"))
            errors = validate_reference(value, name, [p.name for p in files] if grouped else None)
        except (OSError, ValueError) as exc:
            errors = [f"JSON: {exc}"]
        if errors:
            report["invalid"].append({**case, "errors": errors})
        else:
            report["valid"].append(name)
            if value.get("review_status") == "pending_user_review":
                report["pending_review"].append(name)
    for stem, names in references.items():
        if len(names) > 1:
            report["layout_errors"].append(f"Shared reference basename {stem!r}: {names}")
    for path in sorted(input_dir.glob("*.expected.json")):
        stem = path.name.removesuffix(".expected.json").casefold()
        if stem not in references:
            report["orphaned"].append(str(path.resolve()))
    for folder in sorted(input_dir.iterdir()):
        if folder.is_dir():
            nested = [str(p.resolve()) for p in folder.iterdir() if p.is_dir()]
            if nested:
                report["layout_errors"].append(f"Unsupported nested folders: {nested}")
            if not any(name == folder.name for name, _ in jobs):
                report["layout_errors"].append(f"Receipt folder contains no supported images/PDF: {folder.name}")
            for reference in folder.glob("*.expected.json"):
                report["layout_errors"].append(f"Move reference next to its receipt folder: {reference.resolve()}")
    return report


def write_report(directory, report):
    save_json(directory / "report.json", report)
    checks = report["checks"]
    lines = ["# Dane paragonów — zadania do wykonania", "",
             "Przepisz dane bezpośrednio ze zdjęć, według data/input/EXPECTED.md.",
             "Nie kopiuj wyniku testowanego OCR. Zachowaj poprawne wzorce; popraw tylko wskazane przypadki.",
             "Jeden folder to jeden paragon: jeden JSON obok folderu, source = nazwa folderu,",
             "sources = uporządkowane nazwy zdjęć. Nakładających się fragmentów nie przepisuj dwukrotnie.",
             "Oznacz niepewności w uncertain_fields. Użytkownik sprawdza treść po uzupełnieniu.", "",
             f"Poprawne strukturalnie: {len(checks['valid'])}; brak JSON: {len(checks['missing'])}; błędne JSON: {len(checks['invalid'])}.", ""]
    for case in checks["missing"] + checks["invalid"]:
        lines.extend([f"## {case['source']}", f"JSON: `{case['reference']}`", "Zdjęcia:"])
        lines.extend(f"- `{path}`" for path in case["images"])
        lines.extend(f"- Błąd: {error}" for error in case.get("errors", ["Brak JSON"]))
        lines.append("")
    for heading, values in (("Błędy pobierania", report["sync"]["errors"]),
                            ("Błędy układu", checks["layout_errors"]),
                            ("Opisy bez źródeł", checks["orphaned"]),
                            ("Opisy oczekujące na sprawdzenie użytkownika", checks["pending_review"]),
                            ("Brak opcjonalnej transkrypcji TXT", checks["missing_text"])):
        if values:
            lines.extend([f"## {heading}", "", *[f"- {value}" for value in values], ""])
    (directory / "report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--drive-url", default=DEFAULT_DRIVE)
    parser.add_argument("--input-dir", type=Path, default=WORKER / "data/input")
    parser.add_argument("--report-dir", type=Path, default=WORKER / ".cache/dataset")
    parser.add_argument("--check-only", action="store_true", help="Check local references without network access")
    args = parser.parse_args(argv)
    input_dir, directory = args.input_dir.resolve(), args.report_dir.resolve()
    input_dir.mkdir(parents=True, exist_ok=True)
    sync = {"downloaded": [], "existing": [], "ignored": [], "errors": []}
    if not args.check_only:
        print("Listing Drive and downloading new receipts...", flush=True)
        try:
            sync = sync_drive(args.drive_url, input_dir, directory / "sync-state.json")
        except Exception as exc:
            sync["errors"].append(str(exc))
    checks = check_dataset(input_dir)
    write_report(directory, {"input_dir": str(input_dir), "sync": sync, "checks": checks})
    print(f"Downloaded: {len(sync['downloaded'])}; existing: {len(sync['existing'])}")
    print(f"Valid: {len(checks['valid'])}; missing JSON: {len(checks['missing'])}; invalid JSON: {len(checks['invalid'])}")
    print(f"Awaiting user review: {len(checks['pending_review'])}")
    print(f"Report for agent: {directory / 'report.md'}")
    for error in sync["errors"]:
        print(f"Download error: {error}")
    return int(bool(sync["errors"] or checks["missing"] or checks["invalid"] or checks["orphaned"] or checks["layout_errors"]))


if __name__ == "__main__":
    raise SystemExit(main())
