"""Bounded concurrent API execution for a saved presentation OCR batch."""

import hashlib
import json
import sys
import threading
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from time import perf_counter, sleep

from extraction.pipeline import Pipeline
from extraction.steps.parsing import GeminiParseStep
from .gemini import PRICES, load_api_key, request_bound
from .parsing import _restore_ocr
from extraction.steps.parsing.gemini import build_request


def main(root, exclude_tail=False):
    root = Path(root)
    sources = sorted(path for path in (root / "oriented").iterdir() if (path / "ocr.json").is_file())
    if exclude_tail:
        tail = {path.name for path in (root / "gemini-tail-input").iterdir()}
        sources = [path for path in sources if path.name not in tail]
    model = "gemini-3.1-flash-lite"
    key = load_api_key()
    requests = [(path.name, _restore_ocr(json.loads((path / "ocr.json").read_text(encoding="utf-8"))))
                for path in sources]
    reservation = sum(request_bound(build_request(ocr), model) for _, ocr in requests)
    if reservation > 5:
        raise ValueError("Conservative batch reservation exceeds $5")
    log_path = root / ("api-prefill-front.json" if exclude_tail else "api-prefill-all.json")
    records = json.loads(log_path.read_text(encoding="utf-8")) if log_path.exists() else []
    lock = threading.Lock()
    rate_lock = threading.Lock()
    next_request = [0.0]

    def execute(entry):
        name, ocr = entry
        for attempt in range(4):
            step = GeminiParseStep(key, model, Path(__file__).resolve().parents[1] / ".cache" / "gemini")
            body = json.dumps(build_request(ocr), ensure_ascii=False, sort_keys=True).encode("utf-8")
            digest = hashlib.sha256(model.encode() + b"\0" + body).hexdigest()
            if not (step.cache_dir / f"{digest}.json").is_file():
                with rate_lock:
                    sleep(max(0, next_request[0] - perf_counter()))
                    next_request[0] = perf_counter() + 6
            started = perf_counter()
            result = Pipeline([("gemini", step)]).run(ocr)
            response = step.response or {}
            usage = response.get("usageMetadata", {})
            cost = None
            if step.cache_hit:
                cost = 0
            elif "promptTokenCount" in usage and "candidatesTokenCount" in usage:
                cost = float((usage["promptTokenCount"] * PRICES[model][0]
                              + (usage["candidatesTokenCount"] + usage.get("thoughtsTokenCount", 0))
                              * PRICES[model][1]) / 1_000_000)
            record = {"source": name, "attempt": attempt + 1, "ok": result.ok,
                      "cache_hit": step.cache_hit, "seconds": perf_counter() - started,
                      "usage": usage, "estimated_cost_usd": cost,
                      "error": result.error.message if result.error else None}
            with lock:
                records.append(record)
                log_path.write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding="utf-8")
                print(f"{name}: {'ok' if result.ok else record['error']}; cache={step.cache_hit}", flush=True)
            transient = step.api_failed and result.error and any(
                marker in result.error.message for marker in ("HTTP 503", "HTTP 429", "network request failed"))
            if not transient or attempt == 3:
                return
            if "HTTP 429" in result.error.message:
                with rate_lock:
                    next_request[0] = max(next_request[0], perf_counter() + 30)
            sleep((12, 30, 60)[attempt])

    with ThreadPoolExecutor(max_workers=3) as executor:
        list(executor.map(execute, requests))


if __name__ == "__main__":
    main(sys.argv[1], "--exclude-tail" in sys.argv[2:])
