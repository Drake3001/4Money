"""Prepare once; run three paths with one shared lazy OCR engine."""

import json

from extraction.contracts import PipelineError, PipelineResult
from extraction.pipeline import Pipeline
from extraction.reporting import write_environment, write_result, write_summary
from extraction.runner import collect_jobs, new_run, read_sources, run_case
from extraction.steps.docscan import DocscanStep
from extraction.steps.prepare import PrepareDocumentStep
from extraction.steps.ocr import RapidOCRStep
from extraction.steps.uvdoc import UVDocStep

from experiments.evaluation import write_evaluation


def run_preprocessing(input_dir, output_dir, dpi=200, debug=False):
    root = new_run(output_dir)
    prepare = Pipeline([("prepare", PrepareDocumentStep(dpi))])
    ocr = RapidOCRStep()
    variants = {
        "original": Pipeline([("ocr", ocr)]),
        "docscan": Pipeline([("docscan", DocscanStep()), ("ocr", ocr)]),
        "uvdoc": Pipeline([("uvdoc", UVDocStep()), ("ocr", ocr)]),
    }
    results = {name: {} for name in variants}
    for name, files in collect_jobs(input_dir):
        try:
            prepared = prepare.run(read_sources(files), capture_steps=debug)
        except OSError as exc:
            prepared = PipelineResult(
                error=PipelineError("input", type(exc).__name__, str(exc))
            )
        write_result(root / "preparation" / name, prepared)
        for variant, pipeline in variants.items():
            if prepared.ok:
                result = run_case(
                    pipeline, prepared.output, root / variant / name, debug
                )
            else:
                result = PipelineResult(error=prepared.error)
                write_result(root / variant / name, result)
            results[variant][name] = result
            print(f"{variant}/{name}: {'ok' if result.ok else 'error'}", flush=True)
    lines = [
        "# Porównanie preprocessingu",
        "",
        "Audyt ze wzorcami, nie procent dokładności OCR.",
        "",
        "| Wariant | Paragon | Nazwy zgodne | Przybliżone | Brak | Liczby zgodne | Inne |",
        "|---|---|---:|---:|---:|---:|---:|",
    ]
    for variant, cases in results.items():
        directory = root / variant
        directory.mkdir(exist_ok=True)
        write_summary(directory, cases)
        report = write_evaluation(directory, input_dir, cases)
        if report:
            audit = json.loads(report.with_suffix(".json").read_text(encoding="utf-8"))
            for case in audit["cases"]:
                names, numbers = case["name_counts"], case["numeric_field_counts"]
                lines.append(
                    f"| {variant} | {case['source']} | {names.get('match', 0)} | {names.get('approximate', 0)} | {names.get('missing', 0)} | {numbers.get('match', 0)} | {sum(numbers.values()) - numbers.get('match', 0)} |"
                )
        for name, result in cases.items():
            if result.error:
                lines.append(f"\nBłąd ({variant}, {name}): {result.error.message}")
    lines.extend(
        [
            "",
            "Docscan bez narożników może zwrócić obraz bez prostowania. UVDoc może deformować treść.",
            "Ramki OCR dotyczą obrazu po obróbce; oryginalne pliki pozostają zachowane.",
        ]
    )
    with (root / "comparison.md").open("x", encoding="utf-8") as stream:
        stream.write("\n".join(lines) + "\n")
    write_environment(root)
    return root, results
