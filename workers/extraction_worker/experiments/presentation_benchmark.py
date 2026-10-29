"""Fresh orientation ablation and parser comparison on the full local dataset."""

import hashlib
import json
from pathlib import Path

from extraction.pipeline import Pipeline
from extraction.reporting import to_dict, write_environment, write_json, write_result, write_summary
from extraction.runner import collect_jobs, new_run, read_sources
from extraction.steps.ocr import RapidOCRStep
from extraction.steps.orientation import OrientationStep
from extraction.steps.parsing import ParseReceiptStep
from extraction.steps.prepare import PrepareDocumentStep
from extraction.steps.validate import ValidateReceiptStep

from .gemini import run_failed, run_gemini
from .parsing import write_parser_evaluation


def main():
    worker = Path(__file__).resolve().parents[1]
    inputs = worker / "data" / "input"
    root = new_run(worker / "data" / "output")
    print(f"BENCHMARK_ROOT={root}", flush=True)
    for child in ("oriented", "without-orientation", "gemini"):
        (root / child).mkdir()
    prepare = PrepareDocumentStep(pdf_dpi=200)
    orient = OrientationStep()
    ocr = RapidOCRStep()
    pipeline = Pipeline([("ocr", ocr), ("parse", ParseReceiptStep()),
                         ("validate", ValidateReceiptStep())])
    oriented, baseline, cases = {}, {}, []
    jobs = collect_jobs(inputs)
    code_files = [worker / "extraction" / "contracts.py",
                  worker / "extraction" / "steps" / "orientation.py",
                  *sorted((worker / "extraction" / "steps" / "parsing").glob("*.py"))]
    write_json(root / "experiment.json", {
        "documents": [name for name, _ in jobs],
        "design": "Fresh OCR after orientation; fresh no-orientation OCR only for changed images. "
                  "Unchanged image bytes reuse identical OCR and rule prediction in the baseline.",
        "orientation_config": orient.config,
        "code_sha256": {str(path.relative_to(worker)): hashlib.sha256(path.read_bytes()).hexdigest()
                        for path in code_files},
        "input_sha256": {str(path.relative_to(inputs)): hashlib.sha256(path.read_bytes()).hexdigest()
                         for _, paths in jobs for path in paths},
    })
    for index, (name, files) in enumerate(jobs, 1):
        preparation = Pipeline([("prepare", prepare), ("orient", orient)]).run(
            read_sources(files), capture_steps=True)
        if not preparation.ok:
            write_result(root / "oriented" / name, preparation, save_images=False)
            oriented[name] = preparation
            baseline[name] = preparation
            cases.append({"source": name, "error": to_dict(preparation.error)})
            continue
        before = preparation.captured_steps["prepare"]
        after = preparation.output
        changed_pages = [old.number for old, new in zip(before.pages, after.pages, strict=True)
                         if old.content != new.content]
        prediction = pipeline.run(after, capture_steps={"ocr"})
        write_result(root / "oriented" / name, prediction, save_images=False)
        oriented[name] = prediction
        original = pipeline.run(before, capture_steps={"ocr"}) if changed_pages else prediction
        write_result(root / "without-orientation" / name, original, save_images=False)
        baseline[name] = original
        cases.append({"source": name, "changed_pages": changed_pages,
                      "page_count": len(after.pages), "preparation_steps": to_dict(preparation.steps),
                      "orientation_warnings": to_dict(preparation.warnings),
                      "oriented_ok": prediction.ok, "baseline_ok": original.ok,
                      "baseline_reused": not changed_pages})
        if changed_pages:
            for label, document in (("before", before), ("after", after)):
                folder = root / "orientation-images" / name / label
                folder.mkdir(parents=True)
                for page in document.pages:
                    if page.number in changed_pages:
                        (folder / f"{page.number:03d}.png").write_bytes(page.content)
        (root / "orientation-progress.json").write_text(
            json.dumps(cases, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"[{index}/{len(jobs)}] {name}: rotated={changed_pages}; ok={prediction.ok}", flush=True)
    for label, results in (("oriented", oriented), ("without-orientation", baseline)):
        write_summary(root / label, results)
        write_parser_evaluation(root / label, inputs, results)
    write_environment(root)
    write_json(root / "orientation-results.json", cases)
    # This prepares inspectable requests and validates all references before API execution.
    prepared = run_gemini(root / "oriented", inputs, root / "gemini", limit=len(jobs), execute=False)
    reservation = json.loads((prepared / "gemini-run.json").read_text(encoding="utf-8"))[
        "conservative_reservation_usd"]
    print(f"Gemini conservative reservation: ${reservation}; maximum $5", flush=True)
    comparison = run_gemini(root / "oriented", inputs, root / "gemini", limit=len(jobs),
                            execute=True, max_cost_usd="5.00")
    if run_failed(comparison):
        raise RuntimeError(f"Gemini comparison incomplete; saved OCR can be reused: {comparison}")
    write_json(root / "completed.json", {"gemini_run": str(comparison)})
    print(f"COMPLETED={root}", flush=True)


if __name__ == "__main__":
    main()
