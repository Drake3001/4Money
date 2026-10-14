"""Discover tracked backend projects and run VSTest suites without rebuilding."""

import argparse
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

BACKEND = Path("backend/ZPI4M.Backend")
SOLUTION = BACKEND / "ZPI4M.Backend.slnx"


def tracked_files(root):
    output = subprocess.check_output(["git", "ls-files", "-z"], cwd=root)
    return {Path(os.fsdecode(name)) for name in output.split(b"\0") if name}


def summary(message):
    print(message, flush=True)
    if path := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(path, "a", encoding="utf-8") as stream:
            stream.write(message + "\n\n")


def detect(root, tracked):
    present = any(
        path.is_relative_to(BACKEND) and path.suffix in {".cs", ".csproj", ".slnx"}
        for path in tracked
    )
    if present and (SOLUTION not in tracked or not (root / SOLUTION).is_file()):
        raise ValueError("Backend exists but the tracked ZPI4M.Backend.slnx is missing.")
    if not present:
        summary("Backend not merged: build, formatting and tests were not run.")
    if output := os.environ.get("GITHUB_OUTPUT"):
        with open(output, "a", encoding="utf-8") as stream:
            stream.write(f"present={str(present).lower()}\n")
    return present


def projects(root, tracked):
    members = {
        (root / SOLUTION.parent / node.attrib["Path"].replace("\\", "/")).resolve()
        for node in ET.parse(root / SOLUTION).getroot().iter("Project")
    }
    suites = {"unit": [], "integration": []}
    for path in sorted(tracked):
        if not path.is_relative_to(BACKEND / "tests") or path.suffix != ".csproj":
            continue
        if path.name.endswith(".UnitTests.csproj"):
            suite = "unit"
        elif path.name.endswith(".IntegrationTests.csproj"):
            suite = "integration"
        else:
            raise ValueError(
                f"Test project must end in .UnitTests.csproj or .IntegrationTests.csproj: {path}"
            )
        if (root / path).resolve() not in members:
            raise ValueError(f"Test project is not in the solution: {path}")
        suites[suite].append(path)
    return suites


def executed_tests(directory):
    reports = list(directory.rglob("*.trx"))
    if not reports:
        raise ValueError("No TRX report was produced.")
    executed = 0
    for report in reports:
        tree = ET.parse(report)
        result = tree.find(".//{*}ResultSummary")
        counters = tree.find(".//{*}ResultSummary/{*}Counters")
        if counters is None:
            raise ValueError(f"Missing test counters in {report.name}.")
        count = int(counters.attrib.get("executed", "0"))
        if count <= 0:
            raise ValueError(f"Zero executed tests in {report.name}.")
        executed += count
        if (
            int(counters.attrib.get("failed", "0"))
            or int(counters.attrib.get("error", "0"))
            or result.attrib.get("outcome") not in {"Completed", "Passed"}
        ):
            raise ValueError(
                f"Unsuccessful test results in {report.name}: {count} executed tests."
            )
    return executed


def run_suite(root, paths, suite, results):
    if not paths:
        summary(f"{suite.title()} tests: **not configured** (no tracked projects).")
        return 0
    failed = False
    for path in paths:
        directory = results / path.relative_to(BACKEND / "tests").with_suffix("")
        directory.mkdir(parents=True, exist_ok=False)
        result = subprocess.run([
            "dotnet", "test", str(path), "--configuration", "Release",
            "--no-build", "--no-restore", "--logger", "trx;LogFilePrefix=results",
            "--results-directory", str(directory),
        ], cwd=root)
        try:
            count = executed_tests(directory)
            if result.returncode:
                raise ValueError(f"dotnet test exited with code {result.returncode}.")
            summary(f"`{path}`: **passed**, {count} executed tests.")
        except (ValueError, ET.ParseError) as error:
            summary(f"`{path}`: **failed** — {error}")
            failed = True
    return int(failed)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["detect", "unit", "integration"])
    parser.add_argument("--results", type=Path)
    args = parser.parse_args()
    root = Path(
        subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip()
    )
    try:
        tracked = tracked_files(root)
        if args.command == "detect":
            if detect(root, tracked):
                projects(root, tracked)
            return 0
        if not args.results:
            parser.error("--results is required when running tests")
        suites = projects(root, tracked)
        return run_suite(root, suites[args.command], args.command, args.results.resolve())
    except (ValueError, OSError, subprocess.CalledProcessError, ET.ParseError) as error:
        summary(f"Backend checks failed: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
