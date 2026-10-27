"""Read packaged UTF-8 instructions without trimming their whitespace."""

import re
from importlib.resources import files


def load_text(name, *, package="extraction.prompts"):
    if not re.fullmatch(r"[a-z][a-z0-9_]*", name):
        raise ValueError("Prompt name must be a resource identifier")
    return files(package).joinpath(f"{name}.md").read_text(encoding="utf-8")
