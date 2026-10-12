# Local Git hooks

Python 3.10+ and Git are required. Backend C# changes also require a compatible
.NET 10+ SDK. Install the pinned tooling from the repository root:

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements-dev.txt
.venv/bin/python tools/hooks/install.py
```

On Windows, use `.venv\Scripts\python.exe` instead of `.venv/bin/python`.
The installer checks for existing hooks and `core.hooksPath` before installing;
resolve a conflict explicitly rather than overwriting someone else's hooks.
Keep `.venv` available: installed hooks use that Python environment.
The first run downloads pinned hook environments, which are cached afterwards.

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
.venv/bin/pre-commit run --all-files
```

This command can modify files. Review them before staging. The current branch
has no backend C# projects yet, so its .NET hook is skipped until those files
arrive. Hook configuration does not create or merge the backend layout.

`prepare-commit-msg` adds the ClickUp task from the branch before the commit
editor opens; `commit-msg` rejects a task prefix with no description. See
[ClickUp conventions](../clickup.md). Local hooks can be skipped or uninstalled;
they do not replace CI or repository access controls. No GitHub workflow is
changed by this hook setup.

Test the custom hook logic without committing project code:

```bash
python3 -m unittest discover -s tools/hooks/tests -v
```


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
