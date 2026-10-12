"""Format only the C# files supplied by pre-commit; never stage or restore."""

import os
import shutil
import subprocess
import sys
from pathlib import Path

IGNORED = {"bin", "obj", ".git", ".venv", ".terraform"}


def find_solution(root):
    backend = root / "backend"
    solutions = sorted(
        path for path in backend.rglob("*")
        if path.suffix in {".slnx", ".sln"} and not IGNORED.intersection(path.relative_to(backend).parts)
    )
    if len(solutions) != 1:
        raise ValueError("Expected exactly one .slnx/.sln under backend; found " + str(len(solutions)) + ". Merge the backend layout or resolve duplicate solutions.")
    return solutions[0]


def format_files(root, filenames):
    files = []
    for filename in filenames:
        path = (root / filename).resolve()
        if not path.is_relative_to((root / "backend").resolve()):
            raise ValueError("C# formatting is limited to backend files.")
        if path.suffix == ".cs" and path.is_file() and not IGNORED.intersection(path.relative_to(root).parts):
            files.append(path)
    if not files:
        return 0
    solution = find_solution(root)
    if not shutil.which("dotnet"):
        raise ValueError("Install the .NET 10 SDK before committing backend C# changes.")
    environment = {**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1"}
    sdk = subprocess.run(["dotnet", "--version"], cwd=solution.parent, env=environment, capture_output=True, text=True)
    if sdk.returncode or not sdk.stdout.strip().split(".")[0].isdigit() or int(sdk.stdout.strip().split(".")[0]) < 10:
        raise ValueError("A compatible .NET 10+ SDK is required; also check the repository's global.json.")
    includes = ["./" + os.path.relpath(path, solution.parent) for path in files]
    for command in ("style", "whitespace"):
        arguments = ["dotnet", "format", command, str(solution), "--no-restore", "--include", *includes]
        if command == "style":
            arguments.extend(["--severity", "warn"])
        result = subprocess.run(arguments, cwd=solution.parent, env=environment)
        if result.returncode:
            print(f"Formatting failed. Run dotnet restore {solution.relative_to(root)} from the repository root, resolve the reported errors, and retry.", file=sys.stderr)
            return result.returncode
    return 0


def main():
    try:
        result = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True)
        return format_files(Path(result.stdout.strip()), sys.argv[1:])
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
