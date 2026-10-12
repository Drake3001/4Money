#!/usr/bin/env python3
"""Review or apply VM 250-255 ACLs from the Proxmox host; never prints token secrets."""

import argparse
import json
import os
import subprocess


USER = "terraform@pve"
TOKEN = "lab"
ROLE = "TerraformTestVM"
VM_IDS = range(250, 256)
PRIVILEGES = {
    "VM.Allocate", "VM.Audit", "VM.Config.CDROM", "VM.Config.CPU",
    "VM.Config.Cloudinit", "VM.Config.Disk", "VM.Config.HWType",
    "VM.Config.Memory", "VM.Config.Network", "VM.Config.Options", "VM.PowerMgmt",
}
ACL = [
    *((f"/vms/{vm_id}", ROLE) for vm_id in VM_IDS),
    ("/vms/140", "PVETemplateUser"),
    ("/storage/local-lvm", "PVEDatastoreUser"),
    ("/sdn/zones/localnetwork/vmbr0", "PVESDNUser"),
]


def read_json(*command):
    result = subprocess.run(
        [*command, "--output-format", "json"],
        check=True, capture_output=True, text=True,
    )
    return json.loads(result.stdout)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Apply the reviewed ACL changes.")
    args = parser.parse_args()
    if os.geteuid() != 0:
        raise SystemExit("Run on the Proxmox host as root.")

    tokens = read_json("pveum", "user", "token", "list", USER)
    if not any(token["tokenid"] == TOKEN for token in tokens):
        raise SystemExit(f"Expected token {USER}!{TOKEN} not found; no changes made.")
    resources = read_json("pvesh", "get", "/cluster/resources", "--type", "vm")
    occupied = sorted(int(vm["vmid"]) for vm in resources if int(vm["vmid"]) in VM_IDS)
    if occupied:
        raise SystemExit(f"Test VMIDs already in use: {occupied}; no changes made.")
    template = read_json("pvesh", "get", "/nodes/dell/qemu/140/config")
    if not template.get("template") or not template.get("scsi0", "").startswith("local-lvm:"):
        raise SystemExit("Template 140 does not match the reviewed configuration; no changes made.")
    storage = read_json("pvesh", "get", "/nodes/dell/storage/local-lvm/status")
    if not storage.get("active") or storage.get("avail", 0) < 8 * 1024**3:
        raise SystemExit("local-lvm is unavailable or has less than 8 GiB free; no changes made.")

    roles = read_json("pveum", "role", "list")
    existing = next((role for role in roles if role["roleid"] == ROLE), None)
    if existing and set(existing["privs"].split(",")) != PRIVILEGES:
        raise SystemExit(f"Role {ROLE} already exists with different privileges; no changes made.")

    commands = []
    if not existing:
        commands.append(["pveum", "role", "add", ROLE, "--privs", ",".join(sorted(PRIVILEGES))])
    for path, role in ACL:
        for option, principal in (("--users", USER), ("--tokens", f"{USER}!{TOKEN}")):
            commands.append([
                "pveum", "acl", "modify", path, option, principal,
                "--roles", role, "--propagate", "0",
            ])

    print("Scope: VM 250-255 management, template 140 cloning, local-lvm allocation, vmbr0 use.")
    print("VM.Allocate includes deletion of VM 250-255; storage allocation is not a disk quota.")
    print("Existing read-only ACLs are retained. No token, VM, or runner service is created.")
    for command in commands:
        print(" ".join(command))
        if args.apply:
            subprocess.run(command, check=True)
    print("ACL changes applied." if args.apply else "Review only: no changes made. Use --apply after approval.")


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        raise SystemExit(
            f"Proxmox command failed (exit {error.returncode}). Stop and inspect ACLs before retrying."
        ) from None
