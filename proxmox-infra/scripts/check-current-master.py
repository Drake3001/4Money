#!/usr/bin/env python3
"""Reject API errors and skip deployment of superseded master commits."""

import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path


def current_master(repository, commit, token):
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("Invalid repository.")
    if not re.fullmatch(r"[0-9a-f]{40}", commit) or not token:
        raise ValueError("Missing commit or GitHub authentication.")
    request = urllib.request.Request(
        f"https://api.github.com/repos/{repository}/git/ref/heads/master",
        headers={"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"},
    )
    with urllib.request.urlopen(request, timeout=20) as response:
        head = json.load(response)["object"]
    if not isinstance(head, dict) or head.get("type") != "commit" or not re.fullmatch(r"[0-9a-f]{40}", head.get("sha", "")):
        raise ValueError("Invalid master reference response.")
    return head["sha"] == commit


def main():
    try:
        current = current_master(os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_SHA"], os.environ["GH_TOKEN"])
    except (KeyError, ValueError, TypeError, urllib.error.URLError, TimeoutError):
        print("::error::Could not verify current master; deployment is disabled.", file=sys.stderr)
        return 1
    with Path(os.environ["GITHUB_OUTPUT"]).open("a") as handle:
        handle.write("current=" + str(current).lower() + "\n")
    if not current:
        message = "This commit has been superseded on master; deployment was skipped."
        print(message)
        with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a") as handle:
            handle.write(message + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
