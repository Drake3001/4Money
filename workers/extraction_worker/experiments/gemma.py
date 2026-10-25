"""One free Gemma request, using synthetic names or an explicitly selected saved OCR."""

import argparse
import json
import os
from pathlib import Path
from time import perf_counter

from extraction.contracts import OCRResult, PreparedDocument, ReceiptData, ReceiptItem
from extraction.categories import TAXONOMY_VERSION
from extraction.reporting import to_dict, write_json
from extraction.runner import new_run
from extraction.steps.enrich import BASE_URL, FREE_MODEL, GoogleGemmaEnricher, NvidiaGemmaEnricher, OpenRouterEnricher
from extraction.steps.parsing import ParseReceiptStep
from extraction.steps.validate import validate_receipt
from .parsing import _restore_ocr

WORKER_DIR = Path(__file__).resolve().parents[1]


def load_config(env_file=WORKER_DIR / ".env", *, provider="openrouter"):
    if provider not in ("openrouter", "nvidia"):
        raise ValueError("Unsupported configuration provider")
    names = (("NVIDIA_API_KEY",) if provider == "nvidia" else
             ("OPENROUTER_API_KEY", "OPENROUTER_MODEL", "OPENROUTER_BASE_URL"))
    values = {}
    if env_file.is_file():
        for line in env_file.read_text(encoding="utf-8-sig").splitlines():
            name, sep, value = line.strip().partition("=")
            if sep and name.strip() in names:
                value = value.strip()
                if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
                    value = value[1:-1]
                values[name.strip()] = value
    for name in names:
        if os.environ.get(name):
            values[name] = os.environ[name].strip()
    if provider == "nvidia":
        return {"api_key": values.get("NVIDIA_API_KEY", "")}
    return {"api_key": values.get("OPENROUTER_API_KEY", ""),
            "model": values.get("OPENROUTER_MODEL", FREE_MODEL),
            "base_url": values.get("OPENROUTER_BASE_URL", BASE_URL)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ocr", type=Path, help="Existing ocr.json; omit for a synthetic connection test")
    parser.add_argument("--execute", action="store_true", help="Make one free API request (or reuse cache)")
    parser.add_argument("--provider", choices=("openrouter", "google", "nvidia"), default="openrouter")
    args = parser.parse_args()
    if args.ocr:
        ocr = _restore_ocr(json.loads(args.ocr.read_text(encoding="utf-8")))
        parsed = ParseReceiptStep().run(ocr)
        receipt, warnings = parsed.output, parsed.warnings
    else:
        receipt = ReceiptData(items=[ReceiptItem(name="Mleko 3,2%"),
                                     ReceiptItem(name="Woda mineralna"),
                                     ReceiptItem(name="Plyn do naczyń")])
        ocr = OCRResult(PreparedDocument((), ()), (), "synthetic", "1", {})
        warnings = ()
    root = new_run(WORKER_DIR / "data" / "output")
    if args.provider == "nvidia":
        config = load_config(provider="nvidia") if args.execute else {"api_key": "dry-run"}
        client = NvidiaGemmaEnricher(**config, cache_dir=WORKER_DIR / ".cache/gemma-nvidia")
    elif args.provider == "google":
        from .gemini import load_api_key
        client = GoogleGemmaEnricher(load_api_key() if args.execute else "dry-run",
                                    cache_dir=WORKER_DIR / ".cache/gemma-google")
    else:
        config = load_config()
        client = OpenRouterEnricher(**config, cache_dir=WORKER_DIR / ".cache/gemma")
    write_json(root / "request.json", client.build_request(receipt, ocr))
    write_json(root / "parser.json", {"receipt": to_dict(receipt), "warnings": to_dict(warnings),
                                       "validation": to_dict(validate_receipt(receipt))})
    print(f"Model: {client.model}; key configured: yes; execute: {args.execute}")
    if args.execute:
        start = perf_counter()
        try:
            enrichment = client.run(receipt, ocr)
        except (RuntimeError, ValueError, OSError):
            write_json(root / "status.json", {"status": "failed", "model": client.model})
            print(f"Results: {root}")
            raise
        write_json(root / "enrichment.json", enrichment)
        response = client.response or {}
        write_json(root / "status.json", {"status": "completed", "model": client.model, "taxonomy_version": TAXONOMY_VERSION,
                                           "provider": response.get("provider"),
                                           "usage": response.get("usage"),
                                           "cache_hit": client.cache_hit,
                                           "seconds": round(perf_counter() - start, 3),
                                           "items": len(enrichment["items"])})
        print(json.dumps(enrichment, ensure_ascii=False, indent=2))
        print(f"Cache hit: {client.cache_hit}; usage: {json.dumps(response.get('usage'))}")
    print(f"Results: {root}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, RuntimeError) as exc:
        print(f"Cannot run Gemma experiment: {exc}")
        raise SystemExit(1) from None
