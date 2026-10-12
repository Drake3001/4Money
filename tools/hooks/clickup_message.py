"""Prepare a ClickUp commit subject; do not make commits or call ClickUp."""

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

TASK = r"CU-[A-Za-z0-9]+"
BRANCH_TASK = re.compile(rf"^({TASK})(?:/[^\r\n]+)?$")
SUBJECT_TASK = re.compile(rf"(?<![A-Za-z0-9])({TASK})(?![A-Za-z0-9])")


def git(*arguments):
    return subprocess.run(["git", *arguments], capture_output=True, text=True)


def history_operation():
    if os.environ.get("PRE_COMMIT_COMMIT_MSG_SOURCE") in {"merge", "squash", "commit"}:
        return True
    for name in ("MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply"):
        result = git("rev-parse", "--git-path", name)
        if result.returncode == 0 and Path(result.stdout.strip()).exists():
            return True
    return False


def subject_index(lines):
    config = git("config", "--get", "core.commentChar")
    comment = config.stdout.strip() if config.returncode == 0 else "#"
    if comment == "auto":
        comment = "#"
    for index, line in enumerate(lines):
        if line.strip() and not line.lstrip().startswith(comment):
            return index
    return None


def prepare(message, branch):
    match = BRANCH_TASK.fullmatch(branch)
    if not match:
        return message, "Branch has no ClickUp ID; commit message left unchanged."
    task = match[1]
    lines = message.splitlines(keepends=True)
    index = subject_index(lines)
    if index is None:
        return task + " - \n" + message, None
    subject = lines[index].rstrip("\r\n")
    if subject.startswith(("fixup! ", "squash! ")):
        return message, None
    existing = SUBJECT_TASK.search(subject)
    if existing:
        if existing[1] != task:
            raise ValueError("Commit subject refers to a different ClickUp task than the branch.")
        return message, None
    lines[index] = task + " - " + lines[index]
    return "".join(lines), None


def validate(message):
    lines = message.splitlines()
    index = subject_index(lines)
    if index is not None and re.fullmatch(rf"{TASK}\s*(?:-\s*)?", lines[index].strip()):
        raise ValueError("Add a commit description after the ClickUp task ID.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stage", choices=("prepare", "validate"))
    parser.add_argument("message_file", type=Path)
    args = parser.parse_args()
    try:
        with args.message_file.open(encoding="utf-8", newline="") as handle:
            original = handle.read()
        if history_operation():
            return 0
        if args.stage == "validate":
            validate(original)
            return 0
        result = git("symbolic-ref", "--quiet", "--short", "HEAD")
        if result.returncode:
            print("Warning: detached HEAD; commit message left unchanged.", file=sys.stderr)
            return 0
        modified, warning = prepare(original, result.stdout.strip())
        if warning:
            print("Warning: " + warning, file=sys.stderr)
        if modified != original:
            with args.message_file.open("w", encoding="utf-8", newline="") as handle:
                handle.write(modified)
        return 0
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
