# Local Git hooks

Polska instrukcja dla deweloperów: [konfiguracja pre-commit](../notes/precommit_notes.md).

uv and Git are required. The tools project requires Python 3.10+; uv can
download a compatible interpreter if needed. Backend C# changes also require a
compatible .NET 10+ SDK. Install the pinned tooling from the repository root:

```bash
uv sync --project tools --only-group pre-commit --locked
uv run --project tools --only-group pre-commit --locked python tools/hooks/install.py
```

The same commands work on Windows. `--project tools` selects the tools project
without changing the working directory.
The installer checks for existing hooks and `core.hooksPath` before installing;
resolve a conflict explicitly rather than overwriting someone else's hooks.
Keep `tools/.venv` available: installed hooks use that Python environment,
without requiring you to activate it or invoke uv for each commit.
The first run downloads pinned hook environments, which are cached afterwards.

`tools/pyproject.toml` defines the `pre-commit` dependency group, and
`tools/uv.lock` records its resolved dependencies. Both belong in Git;
`tools/.venv` is ignored. The tools project is not a distributable package and
has no runtime dependencies. Future Python backend modules use separate uv
projects and environments. Hook repository versions remain pinned in the root
`.pre-commit-config.yaml`; pre-commit manages their environments separately.

When migrating from the previous pip setup, reinstall the hooks using the
commands above. Confirm that all three files in Git's hooks directory reference
`tools/.venv` before removing the old root `.venv`, and check that no other tools
depend on it. Installation does not delete the old environment automatically.
Routine commands use `--locked` to reject an outdated lockfile. After an
intentional dependency change, run `uv lock --project tools`, review the
manifest and lockfile, then synchronize and reinstall the hooks.

The `pre-commit` stage fixes whitespace and final newlines, preserves Markdown
hard line breaks, checks YAML and merge markers, and detects private keys.
It formats staged backend C# files using `dotnet format style` and `whitespace`
with `--no-restore`. It does not build, run backend tests, or perform full code
analysis. Exactly one `.slnx` or `.sln` is discovered recursively under `backend/`,
so the hook supports both the documented layout and a nested backend solution.
No C# changes means no .NET invocation; missing or ambiguous solutions fail
when C# files are present. Do not treat a successful formatting hook as proof
that code compiles or that every analyzer diagnostic was checked.

After merging the backend layout, run `dotnet restore PATH_TO_SOLUTION` once,
and again after changing package/project dependencies. Restore errors stop the
format hook with a reminder; it never implicitly downloads NuGet packages.
Rules live in `backend/.editorconfig`; nested EditorConfig files can override
them and should be reviewed when integrating the backend branch.

If a formatter changes files, the commit stops. Review the changes, stage the
intended hunks yourself, and retry. Hooks never call `git add`. Pre-commit hides
unstaged tracked changes while checking the index and restores them afterwards;
if formatting conflicts with those changes, it rolls back fixes and restores
your edits. Format and stage the intended hunks manually in that case.

Run all formatting and hygiene hooks, including after changing EditorConfig:

```bash
uv run --project tools --only-group pre-commit --locked pre-commit run --all-files
```

This command can modify files. Review them before staging. The .NET hook is
skipped when there are no C# files. Hook configuration does not create or merge
the backend layout.

`prepare-commit-msg` adds the ClickUp task from the branch before the commit
editor opens; `commit-msg` rejects a task prefix with no description. See
[ClickUp conventions](#clickup-branch-and-commit-conventions). Local hooks can be skipped or uninstalled;
they do not replace CI or repository access controls.

# Backend CI

The workflow runs repository hygiene checks independently of the backend:
whitespace, final newlines, YAML, merge markers, and private-key detection.
It does not execute hook tests or commit-message hooks.

Both jobs run in Docker containers on the existing self-hosted Linux runner.
Repository hygiene uses `ghcr.io/astral-sh/uv:0.9.4-python3.12-bookworm`, which
supplies uv, Python, Git and Bash. Its virtual environment and caches live under
`/tmp` inside the container; CI does not reuse the developer's `tools/.venv`.
The backend uses `mcr.microsoft.com/dotnet/sdk:10.0` and installs Python 3 inside
that container for the CI helper.

The VM needs the GitHub Actions runner and Docker with permission to run
containers; uv, Python and .NET do not need to be installed on the VM for these
checks. GitHub, PyPI, NuGet, the container registries and the SDK image's system
package repositories must be reachable. Containers still use the VM's CPU,
memory and disk. Same-repository PRs targeting master and manual workflow runs
are supported. Change dependency images and installation steps in the workflow,
rather than modifying the VM's language tooling.

Backend detection uses tracked files, ignoring stale untracked build directories.
Before the backend is merged, the summary explicitly reports checks as not run.
Once present, a missing solution fails. The job restores, builds Release once,
verifies style/whitespace, then executes unit and integration projects without
rebuilding. Compiler/analyzer errors and formatting violations fail the check;
other build warnings remain visible without being promoted to errors.

Test projects under `backend/ZPI4M.Backend/tests/` must belong to the solution
and end in `.UnitTests.csproj` or `.IntegrationTests.csproj`. Every test in each
project runs; category annotations are not required. Suites without projects
report **not configured**. Existing projects must produce TRX results with at
least one executed test. Missing reports, zero executed tests, and test failures
fail CI. Available reports are uploaded even after failure and retained for
seven days. Database provisioning is not configured yet; integration tests must
supply their own fixtures and must not rely on a developer's local database.

Equivalent local checks, after the backend is merged:

```bash
dotnet restore backend/ZPI4M.Backend/ZPI4M.Backend.slnx
dotnet build backend/ZPI4M.Backend/ZPI4M.Backend.slnx --configuration Release --no-restore
dotnet format style backend/ZPI4M.Backend/ZPI4M.Backend.slnx --severity warn --verify-no-changes --no-restore
dotnet format whitespace backend/ZPI4M.Backend/ZPI4M.Backend.slnx --verify-no-changes --no-restore
python tools/ci/backend_checks.py detect
python tools/ci/backend_checks.py unit --results .cache/backend-results
python tools/ci/backend_checks.py integration --results .cache/backend-results
```

Use a new or empty results directory for each run; the helper rejects existing
project result directories to prevent stale reports from hiding missing tests.
On Linux, use `python3` if `python` is unavailable. All commands run from the
repository root.


# ClickUp branch and commit conventions

Use the original ClickUp task ID with the `CU-` prefix. The branch may contain
the author's name after a slash; only the task ID is added to commit messages.

```bash
git checkout -b "CU-869fb6e1e/Mikoaj-Kaminski"
git commit -m "Setup dobór odpowiednich narzędzi dla jakości kodu (lintery)"
```

The installed `prepare-commit-msg` hook produces:

```text
CU-869fb6e1e - Setup dobór odpowiednich narzędzi dla jakości kodu (lintery)
```

Bare branches such as `CU-869fb6e1e` are also supported. A matching task ID
already present in the subject is retained without duplication. A different
task ID in the subject is rejected rather than silently rewritten. Commit
bodies and trailers are preserved; Conventional Commits are not required.

On branches without a task ID, such as `configure_runner`, the hook warns and
keeps the message unchanged. Detached HEAD also leaves the message unchanged.
Automatic merge, squash, rebase/cherry-pick/revert operations and retained amend
messages are preserved. `fixup!` and `squash!` subjects remain untouched so
autosquash can match their target commits.

For interactive commits, the editor receives the task prefix. Add a meaningful
description; `commit-msg` rejects a message containing only `CU-...` or
`CU-... -`. Re-running preparation does not add another copy of the ID.

The hook does not contact ClickUp or need an API token. The repository must
already be connected to ClickUp's native GitHub integration for task IDs in
GitHub activity to be linked to tickets. Hooks installed on developer machines
do not run for commits generated on GitHub's servers.
