"""Tesseract CLI adapter; TSV words become positioned OCR fragments."""

import csv
import os
import re
import shutil
import subprocess
from io import StringIO
from pathlib import Path
from tempfile import TemporaryDirectory

from .common import MODEL_ROOT, LocalOCRStep, fragment


class TesseractStep(LocalOCRStep):
    name = "tesseract"

    def __init__(self, command="tesseract", tessdata_dir=None, timeout=180):
        self.command = str(command)
        self.model_dir = Path(tessdata_dir) if tessdata_dir else MODEL_ROOT / "tesseract"
        self.timeout = timeout
        self.version = "not-initialized"
        self.config = {
            "command": self.command,
            "tessdata_dir": str(self.model_dir),
            "model_family": "tessdata_best",
            "languages": "pol+eng",
            "oem": 1,
            "psm": 6,
            "cpu_threads": 4,
            "timeout_seconds": timeout,
        }
        self._initialized = False

    def _call(self, arguments):
        env = dict(os.environ, OMP_THREAD_LIMIT="4")
        result = subprocess.run(
            [self.command, *arguments], capture_output=True,
            timeout=self.timeout, env=env, check=False,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        if result.returncode:
            raise RuntimeError(result.stderr.decode("utf-8", errors="replace").strip() or f"Tesseract exit {result.returncode}")
        return result.stdout.decode("utf-8")

    def initialize(self):
        if self._initialized:
            return
        command = shutil.which(self.command)
        if command is None:
            raise RuntimeError("Tesseract is missing; install Tesseract 5 or configure its executable path")
        self.command = command
        self.version = self._call(["--version"]).splitlines()[0]
        if not re.search(r"tesseract v?5\.", self.version, re.IGNORECASE):
            raise RuntimeError(f"Comparison requires Tesseract 5; found {self.version}")
        for language in ("pol", "eng"):
            if not (self.model_dir / f"{language}.traineddata").is_file():
                raise RuntimeError(f"Missing tessdata_best model: {self.model_dir / (language + '.traineddata')}")
        available = self._call(["--tessdata-dir", str(self.model_dir), "--list-langs"]).splitlines()
        if not {"pol", "eng"}.issubset(set(available)):
            raise RuntimeError("Tesseract cannot load the pol+eng models")
        self._initialized = True

    def recognize(self, content):
        # Windows communicate() writes stdin synchronously before its timed waits.
        # A file avoids an unbounded pipe write; its IO stays inside OCR timing.
        temporary_root = MODEL_ROOT.parent / "tmp" / "tesseract"
        temporary_root.mkdir(parents=True, exist_ok=True)
        with TemporaryDirectory(prefix="page-", dir=temporary_root) as directory:
            image_path = Path(directory) / "page.png"
            image_path.write_bytes(content)
            tsv = self._call([
                str(image_path), "stdout", "--tessdata-dir", str(self.model_dir),
                "-l", "pol+eng", "--oem", "1", "--psm", "6",
                "-c", "tessedit_create_tsv=1",
            ])
        if not tsv.startswith("level\tpage_num\t"):
            raise ValueError("Tesseract did not return a TSV header")
        words = csv.DictReader(StringIO(tsv), delimiter="\t", quoting=csv.QUOTE_NONE)
        lines = []
        for word in words:
            if word["level"] != "5" or not word["text"].strip():
                continue
            score = float(word["conf"])
            if score < 0:
                continue
            x, y, w, h = (int(word[key]) for key in ("left", "top", "width", "height"))
            lines.append(fragment(word["text"], score / 100, ((x, y), (x+w, y), (x+w, y+h), (x, y+h))))
        return lines
