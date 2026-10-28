#!/usr/bin/env bash
# Discover tracked backend projects; let dotnet decide whether tests succeed.
set -euo pipefail

command=${1:-}
case "$command" in
  detect) [[ $# == 1 ]] ;;
  unit|integration) [[ $# == 3 && ${2:-} == --results && -n ${3:-} ]] ;;
  *) echo 'Usage: backend_checks.sh detect | unit|integration --results DIRECTORY' >&2; exit 2 ;;
esac

cd "$(git rev-parse --show-toplevel)"
backend=backend/ZPI4M.Backend
solution=$backend/ZPI4M.Backend.slnx
tracked_file=$(mktemp)
trap 'rm -f -- "$tracked_file"' EXIT
git ls-files -z -- "$backend" > "$tracked_file"
mapfile -d '' -t tracked < "$tracked_file"

summary() {
  printf '%s\n' "$1"
  if [[ -n ${GITHUB_STEP_SUMMARY:-} ]]; then
    printf '%s\n\n' "$1" >> "$GITHUB_STEP_SUMMARY"
  fi
}

if [[ $command == detect ]]; then
  present=false
  solution_tracked=false
  for path in "${tracked[@]}"; do
    case "$path" in *.cs|*.csproj|*.slnx) present=true ;; esac
    if [[ $path == "$solution" ]]; then solution_tracked=true; fi
  done
  if [[ $present == true && ( $solution_tracked != true || ! -f $solution ) ]]; then
    echo '::error::Backend exists but the tracked ZPI4M.Backend.slnx is missing.' >&2
    exit 1
  fi
  if [[ $present == false ]]; then
    summary 'Backend not merged: build, formatting and tests were not run.'
  fi
  if [[ -n ${GITHUB_OUTPUT:-} ]]; then
    printf 'present=%s\n' "$present" >> "$GITHUB_OUTPUT"
  fi
  exit 0
fi

projects=()
for path in "${tracked[@]}"; do
  case "$command:$path" in
    unit:"$backend"/tests/*.UnitTests.csproj|unit:"$backend"/tests/*.Tests.csproj|integration:"$backend"/tests/*.IntegrationTests.csproj)
      projects+=("$path") ;;
  esac
done
if (( ${#projects[@]} == 0 )); then
  summary "$command tests: **not configured** (no tracked projects)."
  exit 0
fi

mkdir -p -- "$3"
results=$(cd -- "$3" && pwd)
summary "$command tests: ${#projects[@]} tracked project(s)."
status=0
for project in "${projects[@]}"; do
  relative=${project#"$backend/tests/"}
  directory=$results/${relative%.csproj}
  mkdir -p -- "$directory"
  if dotnet test "$project" --configuration Release --no-build --no-restore \
      --logger 'trx;LogFilePrefix=results' --results-directory "$directory"; then
    summary "\`$project\`: **dotnet test succeeded**."
  else
    summary "\`$project\`: **dotnet test failed**."
    status=1
  fi
done
exit "$status"
