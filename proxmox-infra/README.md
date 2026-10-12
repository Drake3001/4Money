# Proxmox infrastructure with Terraform

This directory contains the Terraform configuration for the Proxmox lab.
Git records the declared configuration, HCP Terraform stores the shared state,
and GitHub Actions runs configuration checks and produces a plan for review.

Terraform currently reads the available Proxmox nodes and exposes their names
as the `proxmox_nodes` output. It does not manage any VM or LXC resources yet.

## Configuration and state

| File | Purpose |
| --- | --- |
| `backend.tf` | Selects the HCP organization `zpi-proxmox-server` and workspace `proxmox-lab`. |
| `providers.tf` | Declares the Terraform version constraint and the `bpg/proxmox` provider; connection settings come from environment variables. |
| `main.tf` | Declares the node data source and its output. |
| `outputs.tf` | Available for additional outputs; currently empty. |
| `.terraform.lock.hcl` | Records the selected provider version and checksums; keep it in Git. |
| `../.github/workflows/terraform.yml` | Defines the GitHub Actions checks and plan. |

The HCP workspace uses **Local execution mode**. Terraform commands execute on
the developer's computer or the self-hosted runner; HCP stores and locks the
central state. HCP does not connect to Proxmox or automatically apply changes.
Local and CI commands use the same workspace through `backend.tf`.

CI uses Terraform CLI `1.15.9`. The provider constraint is `~> 0.112.0`, with
version `0.112.0` selected by the lockfile. Provider upgrades should be reviewed
along with the resulting lockfile changes.

## Everyday change workflow

1. Create a branch from `master` and edit the Terraform configuration.
2. From this directory, run `terraform fmt -recursive`, `terraform init`, and
   `terraform validate`. Run `terraform plan` locally when you need feedback
   before opening a PR.
3. Push the branch to this repository and open a pull request targeting `master`.
4. Review the **Terraform checks and plan** job, especially the proposed changes
   in the **Terraform Plan** log. Review any proposed creation, replacement, or
   deletion of resources before approving the change.
5. After approval, merge the PR. Merging records the accepted configuration in
   Git; the current pipeline does not deploy it.

## GitHub Actions pipeline

The **Terraform Plan** workflow runs when a PR targeting `master` is opened,
updated with new commits, or reopened. It also supports manual execution through
**Actions → Terraform Plan → Run workflow**, selecting a trusted branch.
Manual execution requires the workflow to exist on the default branch.

The job skips fork PRs. There is no path filter, so all eligible PRs trigger it.
There is no `push` trigger or deployment job: a push without an open PR, or a
merge to `master`, does not start this workflow by itself.

The job uses runner labels `self-hosted`, `Linux`, and `X64`, matching the existing
`dell-runner` on the project's shared VM 205. Commands run in `proxmox-infra/`
using Bash, with a 20-minute job timeout.

| Step | Behavior |
| --- | --- |
| Checkout | Fetches the code without persisting Git credentials. |
| Setup Terraform | Installs the pinned CLI version. Actions are pinned to commit SHAs. |
| Format | Runs `terraform fmt -check -recursive`; fails on formatting differences without editing files. |
| Check required configuration | Checks that required secrets and variables are present and that the TLS flag is `true` or `false`. |
| Init | Connects to HCP and installs providers using the committed lockfile; `-lockfile=readonly` prevents lockfile updates in CI. |
| Validate | Checks configuration validity against the provider schema. |
| Plan | Reads the shared state and Proxmox API, then prints proposed changes without applying them. |

Steps run in order, and a failure stops subsequent steps. Noninteractive flags
prevent the job from waiting for terminal input. The plan is available in the
Actions log; the workflow does not save a plan artifact for later application.

The `terraform-proxmox-lab` concurrency group allows one active workflow run at
a time and does not cancel an active run. New events can replace a pending run.
HCP state locking also coordinates Terraform operations started outside CI;
the plan waits up to five minutes to acquire the state lock.

## Runtime credentials and connection settings

GitHub Actions reads the following repository settings from
**Settings → Secrets and variables → Actions**:

| Kind | Name | Purpose or value |
| --- | --- | --- |
| Secret | `TF_API_TOKEN` | HCP token with access to `proxmox-lab` and the permissions needed for CLI planning and state locking. |
| Secret | `PROXMOX_VE_API_TOKEN` | Proxmox API token in the format `USER@REALM!TOKENID=SECRET`. |
| Variable | `PROXMOX_VE_ENDPOINT` | The root node of the server |
| Variable | `PROXMOX_VE_INSECURE` | `true` while using the current certificate without TLS verification; use `false` with a trusted certificate. |

The workflow maps `TF_API_TOKEN` to `TF_TOKEN_app_terraform_io` for `init` and
`plan`. Proxmox connection settings are passed to the provider for `plan`.
Missing settings cause the job to fail before initialization.

For local commands, authenticate to HCP using `terraform login` or provide
`TF_TOKEN_app_terraform_io` through your secret manager. Supply the same
`PROXMOX_VE_ENDPOINT`, `PROXMOX_VE_INSECURE`, and `PROXMOX_VE_API_TOKEN` environment
variables used by CI. Keep tokens out of source files and shell history.

The runner needs Bash, unzip, HTTPS access to GitHub, Terraform downloads, the
provider registry and HCP, plus network access to the Proxmox API. Its GitHub
Actions Runner installation must support the Node.js runtime used by the pinned
actions.

## Applying changes

The current pipeline ends at `plan`. A successful job means the configuration
is valid and its changes can be planned; it does not mean those changes have
been deployed. Changes to outputs are also only persisted by an apply.

When an apply is explicitly authorized, it can be performed locally from a
clean checkout of the reviewed `master` revision with the same HCP workspace
and connection settings:

```bash
terraform init -input=false -lockfile=readonly
terraform apply
```

`terraform apply` calculates a fresh plan and asks for confirmation. Review that
plan before approving it: infrastructure or state may have changed since the
PR plan. The CI log is not a saved plan that this command reuses.

The current Proxmox token has the `PVEAuditor` role, which supports reads rather
than creating or modifying VMs. Introducing managed resources, expanding API
permissions, importing existing machines, or adding an automated apply job
requires separate approval.

## Access and state handling

Run Terraform only from trusted branches. A plan executes provider code, and
the workflow condition is not a security boundary against someone who can edit
the workflow itself. Repository write access and runner access must be limited
to trusted contributors.

VM 205 is shared: users with root access can inspect job processes and capture
runtime secrets. HCP's central state and locking do not protect tokens on the
runner. Retain the read-only Proxmox role for the current scope and revoke any
exposed token before using its replacement.

Do not commit state, saved plans, credentials, or secret variable files.
`.terraform/`, Terraform state and plan files, `.tfvars`, `.env`, `.envrc`, and
`credentials.tfrc.json` are ignored. Keep `.terraform.lock.hcl` tracked.
Use HCP for shared state rather than exchanging local state files, and preserve
state locking for both local and CI commands.

## References

- [Terraform CLI and HCP Local execution](https://developer.hashicorp.com/terraform/cli/cloud)
- [bpg/proxmox provider configuration](https://github.com/bpg/terraform-provider-proxmox/blob/v0.112.0/docs/index.md)
- [Self-hosted runner labels](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/use-in-a-workflow)
- [GitHub Actions security](https://docs.github.com/en/actions/reference/security/secure-use)
- [Manual workflow execution](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)
