# Proxmox infrastructure with Terraform

This directory contains the Terraform configuration for the Proxmox lab.
Git records the declared configuration, HCP Terraform stores the shared state,
and GitHub Actions checks pull requests and deploys approved master commits.

Terraform reads the available Proxmox nodes and declares two VMs: full clones
of Debian Docker template 300 on `dell`. VM 250 hosts the application and VM
255 hosts OCR. Other VMs and LXC containers remain outside this configuration.
This step defines the machines only; it does not deploy Docker services.

Local verification (2026-11-17): `terraform fmt -check -recursive` and
`git diff --check` passed. `terraform validate` was blocked by a checksum
mismatch between the cached Windows provider and the existing lockfile.
No plan, apply, state change or server operation was performed.

## Configuration and state

| File | Purpose |
| --- | --- |
| `backend.tf` | Selects the HCP organization `zpi-proxmox-server` and workspace `proxmox-lab`. |
| `providers.tf` | Declares the Terraform version constraint and the `bpg/proxmox` provider; connection settings come from environment variables. |
| `main.tf` | Declares the node data source and its output. |
| `app-vm.tf` | Declares application VM 250 and moves the former test resource address. |
| `ocr-vm.tf` | Declares OCR VM 255. |
| `outputs.tf` | Exposes both VMs' IDs, names, nodes and configured addresses. |
| `variables.tf` | Declares and validates the public SSH key supplied at runtime. |
| `scripts/configure-test-access.py` | Reviews or applies the limited test ACLs on the Proxmox host; it is not part of CI. |
| `scripts/check-plan.py` | Validates plan JSON against the approved VM scope without printing its values. |
| `.terraform.lock.hcl` | Records the selected provider version and checksums; keep it in Git. |
| `../.github/workflows/terraform.yml` | Defines PR checks and a speculative plan. |
| `../.github/workflows/terraform-deploy.yml` | Plans and applies the exact master commit after merge. |
| `../.github/workflows/deployment.yml` | Coordinates CI/images, Terraform, and optional application deployment through reusable workflows and needs. |

The HCP workspace uses **Local execution mode**. Terraform commands execute on
the developer's computer or the self-hosted runner; HCP stores and locks the
central state. HCP does not connect to Proxmox or automatically apply changes.
Local and CI commands use the same workspace through `backend.tf`.

CI uses Terraform CLI `1.15.9`. The provider constraint is `~> 0.112.0`, with
version `0.112.0` selected by the lockfile. Provider upgrades should be reviewed
along with the resulting lockfile changes.

## Application and OCR VMs

| Setting | Value |
| --- | --- |
| VMID / name | `250` / `4money-app`; `255` / `4money-ocr` |
| Host / source | `dell` / full clones of Debian Docker template `300` |
| CPU / memory | Each: 2 cores / 4096 MiB (initial values matching template configuration) |
| Disk | Each: 60 GiB on `local-lvm`, `scsi0` |
| Network | VirtIO on `vmbr0`; app `192.168.0.220/24`, OCR `192.168.0.221/24` |
| Gateway / DNS | `192.168.0.1` (assumes the existing LAN router also provides DNS) |
| Guest login | `debian`, using the public key from `TEST_VM_SSH_PUBLIC_KEY` |
| Power state | Started after creation; host autostart enabled |

The `moved` block maps the previous `proxmox_virtual_environment_vm.test`
resource to `proxmox_virtual_environment_vm.app`. Moving the state address
does not itself recreate the VM, but changing the clone source from 140 to
300 is expected to require replacement. The old `terraform-test-250` VM is
explicitly disposable; review its deletion/recreation in the plan. Do not
manually delete it or remove its state. VM 255 must remain available.

Before apply, reserve .220/.221 outside the DHCP pool and confirm host capacity
for both full clones. These are configured static addresses, not observed IPs.
Cloud-init and the guest agent must be installed in template 300; enabling
their Proxmox configuration does not prove they work inside the clones.

The shared access key belongs to VM 205. Store its public part in the GitHub
repository variable `TEST_VM_SSH_PUBLIC_KEY`; keep its private part on VM 205.
Contributors connect to VM 205 through Tailscale, then use SSH to the new VMs
over the LAN. The VMs do not need their own Tailscale registration for this
access path, provided VM 205 can reach them and their SSH service is available.
Guest agent support is enabled, but IP discovery is disabled because addresses
are configured statically. Enabling discovery later requires
verifying the guest installation and reviewing the required API permissions.
Expanding the virtual disk may also require guest
partition and filesystem expansion, depending on the image's cloud-init setup.

## Everyday change workflow

1. Create a branch from `master` and edit the Terraform configuration.
2. From this directory, run `terraform fmt -recursive`, `terraform init`, and
   `terraform validate`. Run `terraform plan` locally when you need feedback
   before opening a PR.
3. Push the branch to this repository and open a pull request targeting `master`.
4. Review the **Terraform checks and plan** job, especially the proposed changes
   in the **Terraform Plan** log. Review any proposed creation, replacement, or
   deletion of resources before approving the change.
5. After approval, merge the PR. The push to `master` starts **Deployment**:
   after successful CI and image publication it calls **Terraform Deploy**, which
   creates a fresh plan, checks its scope, and applies that saved plan. There
   is no second deployment approval. Review deletions and replacements in the PR.

## GitHub Actions pipelines

**Terraform Plan** runs for PRs opened, updated, or reopened against `master`.
Fork PRs are skipped. Its `Terraform checks and plan` job is a required merge
check. It checks runner tools, runs the deployment safeguard tests, formatting,
initialization, validation, and planning, then validates the plan scope. It does
not apply changes. Manual execution remains available for trusted branches.

**Deployment** runs on every push to `master`, including merges, without a
path filter. It calls **Publish images** (including Backend CI), then
**Terraform Deploy**, then its own optional **Deploy OCR and APP** job. The first
two stages use workflow_call; needs orders all stages. Terraform Deploy has no independent push trigger, so it is not run
twice for the same push. Infrastructure runs even while SERVER_DEPLOY_ENABLED
is unset; that variable gates only application containers. There is no polling
job waiting for infrastructure. Terraform Deploy still supports
**Actions → Terraform Deploy → Run workflow** for infrastructure alone, but
only when `master` is selected. Manual execution requires the workflow to be
present on the default branch. Both workflows use `self-hosted`, `Linux`, and
`X64` labels, matching `dell-runner` on VM 205, and run from `proxmox-infra/`.
The PR job has a 20-minute limit; the deployment job has a 45-minute limit.

The deployment runs the following steps in one job:

1. Checkout the exact event commit, check dependencies and credentials, and run
   safeguard tests, `fmt`, `init -lockfile=readonly`, and `validate`.
2. Run `terraform plan -detailed-exitcode -out=deployment.tfplan`. Exit code `0`
   means no changes and skips apply; `1` fails the job; `2` permits further checks.
3. Stream `terraform show -json` to the shared scope checker. Managed resources
   must be bpg/proxmox VMs with known IDs 250–255 on `dell`, both before and after
   a change. Creation, update, deletion, and replacement are allowed. Imports,
   unknown identities, other resource types, and out-of-scope VMs fail the job.
   Data sources and output changes are permitted; even no-op managed resources
   are checked.
4. Confirm that the event commit is still HEAD of `master`. A superseded run
   skips apply and records that fact in the job summary. An API error fails closed.
5. Apply the saved plan, with no replanning or interactive approval. Terraform
   retains HCP state locking and waits up to five minutes for the lock.
6. Remove the temporary plan directory in an `always()` cleanup step.

The readable plan is in **Terraform Plan** step logs, and safe operation counts
are in the run summary. Replacements count as both a create and a delete.
Saved plans may contain sensitive values: they are kept with restrictive file
permissions in a unique runner temporary directory and are not uploaded as
artifacts. Full JSON is never printed by the policy checker. Cleanup runs on
normal completion or failure; a host/process crash may leave temporary files
for an administrator to remove.

Deployment concurrency uses `terraform-proxmox-lab-deploy`, with active runs
never cancelled automatically. GitHub can replace an older pending run with a
newer one; the objective is to converge to current `master`, not deploy every
intermediate commit. A new merge during an active apply is handled by the next
deployment. PR groups are separate, per PR, so PR activity cannot replace a
pending deployment. HCP locking coordinates commands across both workflows
and developer computers; a PR may need rerunning if the lock wait expires.

Protect `master` with one PR approval, stale approvals dismissed after new
commits, the required `Terraform checks and plan` check, an up-to-date branch,
and enforcement for administrators. Force pushes and branch deletion remain
blocked. The first merge containing the server workflow activates it and,
after successful CI and image publication, applies the reviewed configuration.
In particular, merging these VM changes
can replace test VM 250 and create VM 255; no separate apply approval follows.

## Runtime credentials and connection settings

GitHub Actions reads the following repository settings from
**Settings → Secrets and variables → Actions**:

| Kind | Name | Purpose or value |
| --- | --- | --- |
| Secret | `TF_API_TOKEN` | HCP Terraform user or team token with access to `proxmox-lab` and the permissions needed for CLI planning, state writes, and state locking. Organization tokens are not supported by Terraform CLI. |
| Secret | `PROXMOX_VE_API_TOKEN` | Proxmox API token in the format `USER@REALM!TOKENID=SECRET`. |
| Variable | `PROXMOX_VE_ENDPOINT` | Proxmox HTTPS API URL, including port `8006`. |
| Variable | `PROXMOX_VE_INSECURE` | `true` while using the current certificate without TLS verification; use `false` with a trusted certificate. |
| Variable | `TEST_VM_SSH_PUBLIC_KEY` | Single OpenSSH public key used for guest access from VM 205. |

The workflow maps `TF_API_TOKEN` to `TF_TOKEN_app_terraform_io` for `init` and
`plan` and `apply`. Proxmox connection settings are passed to the provider for
planning and application.
The public key is passed as `TF_VAR_test_vm_ssh_public_key` and validated by
Terraform. Missing settings cause the job to fail before initialization.

For local commands, authenticate to HCP using `terraform login` or provide
`TF_TOKEN_app_terraform_io` through your secret manager. Supply the same
`PROXMOX_VE_ENDPOINT`, `PROXMOX_VE_INSECURE`, and `PROXMOX_VE_API_TOKEN` environment
variables used by CI. Also export `TF_VAR_test_vm_ssh_public_key` with the same
public key as the GitHub variable. Keep tokens out of source files and shell history.

To load the public key for local planning without keeping it in a repository file:

```bash
export TF_VAR_test_vm_ssh_public_key="$(gh variable get TEST_VM_SSH_PUBLIC_KEY --repo Drake3001/ZPI-4money)"
```

The public key is not a secret and can appear in Terraform plans and HCP state.
The private key is not supplied to Terraform or GitHub Actions.

The runner needs Bash, Python 3, git, curl, unzip, HTTPS access to GitHub, Terraform downloads, the
provider registry and HCP, plus network access to the Proxmox API. Its GitHub
Actions Runner installation must support the Node.js runtime used by the pinned
actions.

On the Ubuntu runner, an administrator can install the required tools with
`sudo apt-get update` followed by `sudo apt-get install -y unzip curl git python3`.
Install runner dependencies during host maintenance; Terraform jobs run as
the runner's regular user and do not need sudo.

If `setup-terraform` reports that `unzip` cannot be found, check that it is
installed and available on the runner's PATH. If `terraform init` reports
`unauthorized` for HCP, verify the type, validity, and organization access of
the token stored in `TF_API_TOKEN`, then rerun the failed job after updating
the secret. Its value must be the token itself, without a `Bearer` prefix.

## Deployment failures and recovery

After merge, inspect the **Infrastructure** stage of **Deployment**,
including the called **Terraform Deploy**, not just the PR check. Success with
an applied plan means Terraform completed and wrote state to HCP; success with
no changes means the workspace already matches the configuration. A superseded
run is also successful but explicitly reports that it skipped application.

For a failed deployment, fix the cause and use **Run workflow** on `master` or
**Re-run failed jobs** for the current master commit. The entire deployment job
runs again and generates a fresh plan. A rerun of an older commit is skipped
by the HEAD check. Never try to reuse a temporary plan from a failed run.

Terraform does not automatically roll back a partially completed apply. Inspect
Proxmox and HCP state before retrying. If a VM was cloned but not recorded in
state, investigate and explicitly approve recovery rather than blindly
recreating, importing, or deleting it. A stale-plan or lock error stops the job;
there is no automatic `force-unlock` or bypass of state locking. Check that no
operation is active before an administrator investigates a lingering lock.

Local planning remains supported. Reserve local apply for explicitly approved
recovery work, using the reviewed master revision and the same workspace and
credentials. Coordinate with CI to avoid a concurrent deployment. Changes
outside VMID 250–255, new managed resource types, imports, or additional API
permissions require a separate reviewed change to the policy and access scope.

The runner uses the API token and needs no Proxmox root SSH key or extra Linux
sudo privileges to provision VMs. Administrative ACL setup remains separate.

## Test API permissions

The configured principal is `terraform@pve!lab`. Its existing test scope is:

| ACL path | Role | Access |
| --- | --- | --- |
| `/vms/250` through `/vms/255` (six individual ACL paths) | `TerraformTestVM` | Create/delete VMs 250–255, read and edit their configuration, and control their power state. |
| `/vms/140` | `PVETemplateUser` | Read and clone the Debian template; no template modification. |
| `/storage/local-lvm` | `PVEDatastoreUser` | Read storage and allocate disk space. |
| `/sdn/zones/localnetwork/vmbr0` | `PVESDNUser` | Read and use the bridge. |

The custom VM role excludes permission administration, host configuration,
guest-agent commands, migration, snapshots, and backups. `VM.Allocate` includes
deletion of VMs 250–255. Storage allocation privileges are not a per-VM storage quota.
Existing `PVEAuditor` access is retained; write access is not granted to existing
production VM IDs. ACLs are assigned to both the user and token without propagation.
The current token uses `privsep=0`, so its effective privileges follow the user.

**Before using template 300**, an administrator must verify/grant
`PVETemplateUser` on `/vms/300` to the user/token as appropriate to token
privilege separation. The existing ACL on `/vms/140` does not grant access to
300. This configuration-only step does not change server ACLs. VMIDs 250 and
255 are already within the existing 250–255 policy scope.

This reserves an automation permission scope, not the VMIDs themselves: an
administrator can still allocate a VMID in this range. Check the inventory
before creating a new VM. The Terraform configuration declares VM 250 and
VM 255; adding ACLs does not create VMs.

The following script documents the original setup for template 140 and an
empty VM range. Do not rerun it for this migration: VM 250 already exists,
and it does not configure cloning access to template 300.
For reference, its review-only invocation from the repository root is:

```bash
ssh -F /dev/null root@192.168.0.232 python3 - < proxmox-infra/scripts/configure-test-access.py
```

After approval, add `--apply` after `python3 -` to apply the reviewed ACLs.
This is an administrative ACL operation, separate from `terraform apply`; it
does not create a VM or a token. The script checks the source template, storage,
token identity, custom role definition, and availability of VMIDs 250–255 before
making changes. It refuses to run if any VMID in that range is already occupied.
If a command fails midway, inspect the ACLs before retrying; changes are not
automatically rolled back. Runner authentication still requires the token value
in the GitHub repository secret `PROXMOX_VE_API_TOKEN`.

## Access and state handling

Run Terraform only from trusted branches. A plan executes provider code, and
the workflow condition is not a security boundary against someone who can edit
the workflow itself. Repository write access and runner access must be limited
to trusted contributors.

VM 205 is shared: users with root access can inspect job processes and capture
runtime secrets. HCP's central state and locking do not protect tokens on the
runner. Keep write privileges within the test scope and revoke any exposed token
before using its replacement.

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
