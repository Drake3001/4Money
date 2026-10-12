# Proxmox infrastructure with Terraform

This directory contains the Terraform configuration for the Proxmox lab.
Git records the declared configuration, HCP Terraform stores the shared state,
and GitHub Actions runs configuration checks and produces a plan for review.

Terraform reads the available Proxmox nodes and declares one test VM: a full
clone of Debian template 140 on `dell`, with VMID 250. Existing VMs and LXC
containers remain outside this configuration.

## Configuration and state

| File | Purpose |
| --- | --- |
| `backend.tf` | Selects the HCP organization `zpi-proxmox-server` and workspace `proxmox-lab`. |
| `providers.tf` | Declares the Terraform version constraint and the `bpg/proxmox` provider; connection settings come from environment variables. |
| `main.tf` | Declares the node data source and its output. |
| `test-vm.tf` | Declares the test VM and its cloud-init settings. |
| `outputs.tf` | Exposes the test VM's ID, name, and node. |
| `variables.tf` | Declares and validates the public SSH key supplied at runtime. |
| `scripts/configure-test-access.py` | Reviews or applies the limited test ACLs on the Proxmox host. |
| `.terraform.lock.hcl` | Records the selected provider version and checksums; keep it in Git. |
| `../.github/workflows/terraform.yml` | Defines the GitHub Actions checks and plan. |

The HCP workspace uses **Local execution mode**. Terraform commands execute on
the developer's computer or the self-hosted runner; HCP stores and locks the
central state. HCP does not connect to Proxmox or automatically apply changes.
Local and CI commands use the same workspace through `backend.tf`.

CI uses Terraform CLI `1.15.9`. The provider constraint is `~> 0.112.0`, with
version `0.112.0` selected by the lockfile. Provider upgrades should be reviewed
along with the resulting lockfile changes.

## Test VM

| Setting | Value |
| --- | --- |
| VMID / name | `250` / `terraform-test-250` |
| Host / source | `dell` / full clone of Debian template `140` |
| CPU / memory | 2 cores / 2048 MiB |
| Disk | 8 GiB on `local-lvm`, `scsi0` |
| Network | VirtIO on `vmbr0`, IPv4 via DHCP |
| Guest login | `debian`, using the public key from `TEST_VM_SSH_PUBLIC_KEY` |
| Power state | Stopped after creation; host autostart disabled |

The shared access key belongs to VM 205. Store its public part in the GitHub
repository variable `TEST_VM_SSH_PUBLIC_KEY`; keep its private part on VM 205.
Contributors connect to VM 205 through Tailscale, then use SSH to the test VMs
over the LAN. The test VMs do not need their own Tailscale registration for this
access path, provided VM 205 can reach them and their SSH service is available.
Guest agent support is enabled and requires the QEMU guest agent in the image.
To start the VM through Terraform, change `started` to `true` and review and
apply the resulting plan. Expanding the virtual disk may also require guest
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
| Variable | `PROXMOX_VE_ENDPOINT` | Proxmox HTTPS API URL, including port `8006`. |
| Variable | `PROXMOX_VE_INSECURE` | `true` while using the current certificate without TLS verification; use `false` with a trusted certificate. |
| Variable | `TEST_VM_SSH_PUBLIC_KEY` | Single OpenSSH public key used for guest access from VM 205. |

The workflow maps `TF_API_TOKEN` to `TF_TOKEN_app_terraform_io` for `init` and
`plan`. Proxmox connection settings are passed to the provider for `plan`.
The public key is passed as `TF_VAR_test_vm_ssh_public_key` and validated by
Terraform. Missing settings cause the job to fail before initialization.

For local commands, authenticate to HCP using `terraform login` or provide
`TF_TOKEN_app_terraform_io` through your secret manager. Supply the same
`PROXMOX_VE_ENDPOINT`, `PROXMOX_VE_INSECURE`, and `PROXMOX_VE_API_TOKEN` environment
variables used by CI. Also export `TF_VAR_test_vm_ssh_public_key` with the same
public key as the GitHub variable. Keep tokens out of source files and shell history.

To load the public key for local planning without keeping it in a repository file:

```bash
export TF_VAR_test_vm_ssh_public_key="$(gh variable get TEST_VM_SSH_PUBLIC_KEY --repo Drake3001/zpi)"
```

The public key is not a secret and can appear in Terraform plans and HCP state.
The private key is not supplied to Terraform or GitHub Actions.

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

The runner needs no Proxmox administrative SSH access or additional Linux sudo
permissions to provision this VM. The provider uses its API token to clone and
configure the VM. Introducing further managed resources, expanding API
permissions, importing existing machines, or adding an automated apply job
requires separate approval.

## Test API permissions

The configured principal is `terraform@pve!lab`. Its approved test scope is:

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

This reserves an automation permission scope, not the VMIDs themselves: an
administrator can still allocate a VMID in this range. Check the inventory
before creating a new VM. The Terraform configuration currently declares only
VM 250; adding ACLs for 251–255 does not create those VMs.

Review the commands from the repository root without making changes:

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
