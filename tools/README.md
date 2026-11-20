# EF Core migrations

The repository pins `dotnet-ef` 10.0.4 in [`dotnet-tools.json`](../dotnet-tools.json),
matching EF Core Design. Restore local tools once after cloning or after a
manifest update:

```bash
dotnet tool restore
```

The small C# wrapper requires .NET 10 SDK and needs no Python, separate project,
or global tool installation. From the repository root:

```bash
dotnet run --file tools/ef.cs -- migrations list
dotnet run --file tools/ef.cs -- migrations has-pending-model-changes
dotnet run --file tools/ef.cs -- migrations add MigrationName
dotnet run --file tools/ef.cs -- database update
```

The wrapper supplies `ZPI4M.Database`, startup `ZPI4M.Api` and `AppDbContext`.
It runs EF in the API directory so the host can load `containers/local/.env`
using a fixed relative path outside Docker. Exported variables take precedence
over that file (`NoClobber`). To select another database for
`migrations list` or `database update`, pass EF's explicit `--connection` option.
Relative EF paths, including explicit project
paths, are resolved from that directory. When calling from a subdirectory,
adjust the path to `tools/ef.cs`. Checkout paths containing spaces are supported.
Explicit `--project/-p`, `--startup-project/-s` and `--context/-c` override the
defaults. Other arguments and exit codes pass through; arguments after `--`
are forwarded to the startup application. No arguments or `--help` shows wrapper
help; `migrations --help` shows EF help.

`migrations list` checks which migrations the configured database has applied;
`migrations has-pending-model-changes` compares the model to the snapshot and
does not apply migrations. `database update` changes the configured database.
Prepare SQL init before migrating a new database, then seed only if intended.
See [development instructions](../agent-context/development.md#modele-i-migracje)
for configuration and the existing migration chain.

Tool restore uses the exact manifest version; it has no version override.
To try another tool version without changing the manifest:

```bash
dotnet run --file tools/ef.cs -- --ef-version 10.0.5 migrations list
```

This uses `dotnet tool exec dotnet-ef@10.0.5 --yes`, downloading the explicitly
selected tool without an interactive confirmation if necessary.
Normal invocations still use the pinned version. To deliberately update the
team's pin, use `dotnet tool update dotnet-ef --local --version VERSION` and
review the manifest diff. Tool version, SDK version and application runtime
are separate: changing the tool does not install an SDK/runtime or update EF
packages. Keep the tool compatible with the application's EF Core version.
There is no new `global.json`; SDK selection remains unchanged.

# Architecture diagrams (ProjGraph)

ProjGraph.Cli 1.2.0 is pinned as a repository-local .NET tool in
[`dotnet-tools.json`](../dotnet-tools.json). It is a development dependency,
separate from application `PackageReference` entries; it is not included in
backend build or publish output. Requires the .NET 10 SDK or later.

From the repository root, restore the tool after cloning:

```bash
dotnet tool restore
dotnet tool run projgraph --version
```

Generate diagrams without building the backend or connecting to a database:

```bash
# Solution project references (includes test projects)
dotnet tool run projgraph visualize backend/ZPI4M.Backend/ZPI4M.Backend.slnx --output .cache/projgraph/projects.md

# Whole-system EF model represented by the migration snapshot
dotnet tool run projgraph erd backend/ZPI4M.Backend/src/ZPI4M.Database/Migrations/AppDbContextModelSnapshot.cs --output .cache/projgraph/database.md

# Focused class diagram, including interfaces and discovered dependencies
dotnet tool run projgraph classdiagram backend/ZPI4M.Backend/src/modules/ZPI4M.Notifications/Services/Notifications/NotificationService.cs --inheritance --dependencies --depth 1 --output .cache/projgraph/notification-service.md
```

The output directories are created automatically. `.md` outputs contain fenced
Mermaid diagrams; use `.mmd` for raw Mermaid. `.cache/` is already ignored by
Git. View Markdown in a Mermaid-capable viewer.

These are static source views: project references do not prove runtime calls,
and the snapshot does not prove the deployed database matches it. Class
dependency discovery uses heuristics; review generated diagrams before publishing.
See the [CLI reference](https://handys11.github.io/ProjGraph/src/ProjGraph.Cli/README.html)
for available options. No documentation publishing workflow is configured here.

To intentionally upgrade, run `dotnet tool update --local ProjGraph.Cli --version VERSION`
and review the manifest change. CI jobs that need diagrams can explicitly run
`dotnet tool restore`; ordinary backend builds do not need this tool.

# Local Git hooks

Polska instrukcja dla deweloperów: [konfiguracja pre-commit](../notes/PRECOMMIT.md).

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
after a successful solution restore, then uses `--no-restore` for both formatting passes.
Automatic import removal (IDE0005) is excluded to preserve necessary imports. It does not build, run backend tests, or perform full code
analysis. Exactly one `.slnx` or `.sln` is discovered recursively under `backend/`,
so the hook supports both the documented layout and a nested backend solution.
No C# changes means no .NET invocation; missing or ambiguous solutions fail
when C# files are present. Do not treat a successful formatting hook as proof
that code compiles or that every analyzer diagnostic was checked.

The hook restores NuGet dependencies before formatting and may download packages.
A restore failure stops the hook before formatting; resolve the restore error and retry.
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

# Tink Connector fixtures

The [Python CLI](scripts/tink_connector.py) seeds synthetic Data v2 accounts
and transactions without the 4Money backend. Its
[Polish guide](scripts/tink_connector/README.md) covers configuration, presets,
durable run manifests, recovery and local HTTP tests. Use both dependency
groups in the same tools environment to retain installed Git hooks:

```bash
uv sync --project tools --group pre-commit --group connector --locked
uv run --project tools --group pre-commit --group connector --locked python tools/scripts/tink_connector.py plan --preset basic-pln
uv run --project tools --group pre-commit --group connector --locked python -m unittest discover -s tools/scripts/tests -v
```

Copy [tools/.env.example](.env.example) to the ignored `tools/.env` and supply
credentials of a Tink test application before `seed`. Run the Python script
directly with uv, supplying `--env-file tools/.env` before `python` when needed.
The Google Fire CLI accepts a run ID for `resume`/`verify` or an explicit
`--manifest`. State lives in ignored `.cache/tink-connector/`;
keep manifests to locate created resources. API entitlement and a live Tink run
are separate from the passing local tests. No update/delete or cleanup commands
are provided.

To refresh Tink identities from a JSON mapping while preserving existing database
data, use [`tink_profiles.py`](scripts/tink_profiles.py). Its [guide](scripts/tink_profiles/README.md)
covers `plan`, `apply`, and `resume`. Edit the default `users.json`, then run:

```bash
uv run --project tools --group pre-commit --group connector --locked \
  --env-file tools/.env python tools/scripts/tink_profiles.py apply
```

The database must already be running with its schema applied. Each batch creates
fresh synthetic Tink profiles and updates only the selected users' `tink_user_id`.
Failed batches stop at the first error and resume from saved manifests.

For a combined local reset, use
[`reset_state.py`](scripts/reset_state.py): it runs `database/seed_local.sql`,
creates fresh Tink fixtures, and binds their generated IDs using a JSON user
mapping. The default mapping includes only `postman.banking`.
The [reset guide](scripts/reset_state/README.md) covers `plan`, `reset`, `resume`,
configuration and disposable PostgreSQL tests. A reset replaces local application
data and sessions; previous remote Tink fixtures remain.

# Backend CI

The workflow runs repository hygiene checks independently of the backend:
whitespace, final newlines, YAML, merge markers, and private-key detection.
It does not execute hook tests or commit-message hooks.

Both jobs run in Docker containers as UID/GID 1000:1000, matching the existing
self-hosted runner account mikka. Each job creates a writable temporary home before
checkout. Backend CLI state and NuGet packages also live under /tmp.
Before rollout, stop the idle runner service, repair ownership of its _work directory
with `sudo chown -R mikka:mikka /home/mikka/actions-runner/_work`, then restart it.
Do not run this ownership repair while jobs are active.
Repository hygiene uses `ghcr.io/astral-sh/uv:0.9.4-python3.12-bookworm`, which
supplies uv, Python, Git and Bash. Its virtual environment and caches live under
`/tmp` inside the container; CI does not reuse the developer's `tools/.venv`.
The backend uses `mcr.microsoft.com/dotnet/sdk:10.0` with native .NET commands
and a small Bash helper; it does not install Python or system packages.
Both jobs trust only the checked-out workspace through Git safe.directory
after checkout, keeping credentials persistence disabled.

The VM needs the GitHub Actions runner and Docker with permission to run
containers; uv, Python and .NET do not need to be installed on the VM for these
checks. GitHub, PyPI, NuGet and the container registries must be reachable.
Containers still use the VM's CPU, memory and disk. Same-repository PRs targeting master and manual workflow runs
are supported. Change dependency images and installation steps in the workflow,
rather than modifying the VM's language tooling.

Backend detection uses tracked files, ignoring stale untracked build directories.
Before the backend is merged, the summary explicitly reports checks as not run.
Once present, a missing solution fails. The job restores, builds Release once,
verifies style/whitespace, then executes unit and integration projects without
rebuilding. Compiler/analyzer errors and formatting violations fail the check;
other build warnings remain visible without being promoted to errors.

Test projects under `backend/ZPI4M.Backend/tests/` must belong to the solution
and end in `.UnitTests.csproj` or `.IntegrationTests.csproj`. The Bash helper
selects tracked projects by those suffixes, without parsing solution XML.
Projects with other names are not selected. Every test in each selected project
runs; category annotations are not required. Suites without projects report
**not configured**. Success follows the exit code of `dotnet test`; empty
projects may pass. Reports are not parsed or required for success. All selected
projects are attempted, even after a project fails, and integration tests run
after unit or formatting failures when the build succeeded. Available TRX
reports are uploaded even after failure and retained for seven days. Database
provisioning is not configured yet; integration tests must supply their own fixtures and must not rely on a developer's local database.

Equivalent local checks, after the backend is merged:

```bash
dotnet restore backend/ZPI4M.Backend/ZPI4M.Backend.slnx
dotnet build backend/ZPI4M.Backend/ZPI4M.Backend.slnx --configuration Release --no-restore
dotnet format style backend/ZPI4M.Backend/ZPI4M.Backend.slnx --severity warn --exclude-diagnostics IDE0005 --verify-no-changes --no-restore
dotnet format whitespace backend/ZPI4M.Backend/ZPI4M.Backend.slnx --verify-no-changes --no-restore
bash tools/ci/backend_checks.sh detect
bash tools/ci/backend_checks.sh unit --results .cache/backend-results
bash tools/ci/backend_checks.sh integration --results .cache/backend-results
```

Use a fresh results directory for each run to keep reports separate. CI creates
one automatically and uses separate subdirectories for each project. All
commands run from the repository root. The helper reports project availability
and command outcomes, not executed-test counts. Restore and build the solution
after adding test projects; solution membership is a convention enforced by
.NET build/test behavior rather than custom XML validation.


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
