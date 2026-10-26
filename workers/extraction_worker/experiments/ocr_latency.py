"""Local CPU-only OCR profiling on frozen, prepared study pages."""

import argparse
import gc
import json
from difflib import SequenceMatcher
from pathlib import Path
from time import perf_counter

from extraction.steps.ocr import RapidOCRStep
from .latency import write_json


def run(root, threads, repeats):
    plan = json.loads((root / "plan.json").read_text(encoding="utf-8"))
    pages = [(c["source"], (root / c["source"] / "page-1.png").read_bytes()) for c in plan["cases"]]
    records = []
    baseline = {}
    for count in threads:
        adapter = RapidOCRStep()
        adapter.initialize()
        engine = adapter._engine
        # Experimental adapter only; same model selection/preprocessing as production.
        # RapidOCR lazily creates ONNX sessions on its first call.
        engine.cfg.EngineConfig.onnxruntime.intra_op_num_threads = count
        start = perf_counter()
        engine(pages[0][1])
        warmup = perf_counter() - start
        for repeat in range(repeats):
            for source, image in pages:
                start = perf_counter()
                output = engine(image)
                seconds = perf_counter() - start
                texts = list(output.txts or ())
                scores = [float(s) for s in output.scores] if output.scores is not None else []
                boxes = output.boxes.tolist() if output.boxes is not None else []
                if source not in baseline:
                    baseline[source] = (texts, boxes)
                previous_texts, previous_boxes = baseline[source]
                record = {"source": source, "threads": count, "repeat": repeat,
                          "seconds": seconds, "substage_seconds": output.elapse_list,
                          "texts": texts, "scores": scores, "boxes": boxes,
                          "same_text_as_first_variant": texts == previous_texts,
                          "same_boxes_as_first_variant": boxes == previous_boxes,
                          "text_similarity_to_first_variant": SequenceMatcher(None, "\n".join(previous_texts), "\n".join(texts), autojunk=False).ratio(),
                          "variant_warmup_seconds": warmup}
                records.append(record)
                write_json(root / "ocr-thread-study.json", {"baseline_threads": threads[0], "repeats": repeats,
                    "model_version": adapter.version, "settings": adapter.config, "records": records})
                print(json.dumps({k: record[k] for k in ("source", "threads", "repeat", "seconds", "substage_seconds", "same_text_as_first_variant", "same_boxes_as_first_variant")}), flush=True)
        del engine, adapter
        gc.collect()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--threads", type=int, nargs="+", default=[4, 2, 1])
    parser.add_argument("--repeats", type=int, choices=range(1, 4), default=2)
    args = parser.parse_args()
    if any(not 1 <= t <= 8 for t in args.threads):
        parser.error("Threads must be 1–8")
    run(args.root, args.threads, args.repeats)


if __name__ == "__main__":
    main()
