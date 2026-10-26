"""Default instructions, shared rendering and prompt provenance.

Overrides replace the complete rendered instruction, including its schema and
categories where applicable. They never change response validation.
"""

import hashlib
import json

from ..categories import category_prompt
from .loader import load_text


def classification_prompt():
    return load_text("classify_items") + "\n" + category_prompt()


def image_analysis_prompt(schema, *, with_ocr=False):
    name = "analyze_receipt_images_with_ocr" if with_ocr else "analyze_receipt_images"
    return (load_text(name) + category_prompt()
            + "\nSchemat odpowiedzi:\n" + json.dumps(schema, ensure_ascii=False))


def resolve_prompt(prompt, default):
    if prompt is None:
        return default
    if not isinstance(prompt, str) or not prompt.strip():
        raise ValueError("Prompt must be nonempty text")
    return prompt


def prompt_metadata(text, identifier):
    return {"prompt_id": identifier,
            "prompt_sha256": hashlib.sha256(text.encode("utf-8")).hexdigest()}
