"""Engine-independent reading order and explicitly eligible transcription metrics."""

import re
import unicodedata
from collections import Counter
from statistics import median

from extraction.steps.parsing.geometry import reconstruct_rows


def transcription_text(raw):
    return "\n".join(line["text"] for page in raw["pages"] for line in page["lines"])


def normalize_transcription(text):
    return re.sub(r"\s+", " ", unicodedata.normalize("NFC", text)).strip()


def edit_distance(expected, observed):
    try:
        from rapidfuzz.distance import Levenshtein
    except ImportError:
        # Exact two-row fallback keeps evaluation usable with the base environment.
        if len(expected) < len(observed):
            expected, observed = observed, expected
        previous = list(range(len(observed)+1))
        for i, a in enumerate(expected, 1):
            current = [i]
            for j, b in enumerate(observed, 1):
                current.append(min(current[-1]+1, previous[j]+1, previous[j-1]+(a != b)))
            previous = current
        return previous[-1]
    return Levenshtein.distance(expected, observed)


def evaluate_cer(reference, expected_text, observed_text):
    if reference.get("transcription_scope") != "full_image":
        return {"status": "not_evaluated", "reason": "transcription_scope is not full_image"}
    expected = normalize_transcription(expected_text or "")
    if not expected:
        return {"status": "not_evaluated", "reason": "Missing or empty expected TXT"}
    observed = normalize_transcription(observed_text)
    errors = edit_distance(expected, observed)
    return {"status": "evaluated", "errors": errors, "reference_characters": len(expected),
            "observed_characters": len(observed), "cer": errors / len(expected)}


def aggregate_cases(cases):
    names, numbers = Counter(), Counter()
    errors = characters = cer_cases = 0
    for case in cases:
        audit = case.get("audit", {})
        names.update(audit.get("name_counts", {}))
        numbers.update(audit.get("numeric_field_counts", {}))
        cer = case["cer"]
        if cer["status"] == "evaluated":
            errors += cer["errors"]
            characters += cer["reference_characters"]
            cer_cases += 1
    return {
        "case_count": len(cases), "name_counts": dict(names),
        "numeric_field_counts": dict(numbers), "cer_case_count": cer_cases,
        "cer": errors / characters if characters else None,
    }


def summarize_variants(variants):
    """Compare completed engines on identical sets, keeping coverage explicit."""
    available = [value for value in variants.values() if value["execution"]["status"] == "available"]
    timing_sets = [
        {name for name, timing in value["execution"]["timings"].items()
         if timing["median_seconds"] is not None}
        for value in available
    ]
    common_timings = set.intersection(*timing_sets) if timing_sets else set()
    common_cases = {}
    for status in ("reviewed", "unreviewed"):
        common_cases[status] = {}
        for metric in ("audit", "cer"):
            sets = [
                {case["source"] for case in value["cases"]
                 if case["reference_status"] == status and
                 ("audit" in case if metric == "audit" else case["cer"]["status"] == "evaluated")}
                for value in available
            ]
            common_cases[status][metric] = sorted(set.intersection(*sets)) if sets else []

    for value in variants.values():
        timings = value["execution"]["timings"]
        own_times = [item["median_seconds"] for item in timings.values()
                     if item["median_seconds"] is not None]
        common_times = [timings[name]["median_seconds"] for name in common_timings
                        if name in timings and timings[name]["median_seconds"] is not None]
        value.update(
            successful_documents=sum(case["status"] == "ok" for case in value["cases"]),
            mean_document_seconds=sum(own_times) / len(own_times) if own_times else None,
            median_document_seconds=median(own_times) if own_times else None,
            common_median_document_seconds=median(common_times) if common_times else None,
        )
        value["groups"], value["common_groups"] = {}, {}
        for status in ("reviewed", "unreviewed"):
            cases = [case for case in value["cases"] if case["reference_status"] == status]
            value["groups"][status] = aggregate_cases([case for case in cases if "audit" in case])
            value["common_groups"][status] = {
                metric: aggregate_cases([case for case in cases if case["source"] in sources])
                for metric, sources in common_cases[status].items()
            }
    return sorted(common_timings), common_cases
