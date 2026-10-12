#!/usr/bin/env python3
"""Validate Terraform plan JSON from stdin without printing its values."""

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

PROVIDER = "registry.terraform.io/bpg/proxmox"
VM_IDS = range(250, 256)
ALLOWED_ACTIONS = {
    ("no-op",), ("create",), ("update",), ("delete",),
    ("create", "delete"), ("delete", "create"),
}


class RejectedPlan(ValueError):
    pass


def validate_plan(plan):
    if not isinstance(plan, dict) or not str(plan.get("format_version", "")).startswith("1."):
        raise RejectedPlan("Unsupported or missing Terraform plan format.")
    if not isinstance(plan.get("planned_values"), dict):
        raise RejectedPlan("Missing planned values; expected a Terraform plan, not state JSON.")
    if plan.get("errored") or plan.get("complete") is False:
        raise RejectedPlan("Errored or incomplete plans cannot be deployed.")
    if plan.get("action_invocations") or plan.get("deferred_changes"):
        raise RejectedPlan("Provider actions and deferred changes are outside the VM policy.")
    resources = plan.get("resource_changes", [])
    if not isinstance(resources, list):
        raise RejectedPlan("Invalid resource changes.")
    counts = Counter(create=0, update=0, delete=0, replacement=0)
    vm_ids = set()
    for resource in resources:
        if not isinstance(resource, dict):
            raise RejectedPlan("Invalid resource change.")
        if resource.get("mode") == "data":
            continue
        if (
            resource.get("mode") != "managed"
            or resource.get("type") != "proxmox_virtual_environment_vm"
            or resource.get("provider_name") != PROVIDER
        ):
            raise RejectedPlan("Only bpg/proxmox VM resources are permitted.")
        change = resource.get("change")
        if not isinstance(change, dict) or not isinstance(change.get("actions"), list):
            raise RejectedPlan("Invalid managed resource action.")
        actions = tuple(change["actions"])
        if actions not in ALLOWED_ACTIONS or change.get("importing") is not None:
            raise RejectedPlan("Unsupported action or import; manual review is required.")
        sides = ("after",) if actions == ("create",) else ("before",) if actions == ("delete",) else ("before", "after")
        for side in sides:
            value = change.get(side)
            if not isinstance(value, dict):
                raise RejectedPlan("VM identity is missing or unknown.")
            vm_id = value.get("vm_id")
            if type(vm_id) is not int or vm_id not in VM_IDS or value.get("node_name") != "dell":
                raise RejectedPlan("VM identity must be known: VMID 250-255 on dell.")
            unknown = change.get("after_unknown", {}) if side == "after" else {}
            if not isinstance(unknown, dict) or unknown.get("vm_id") or unknown.get("node_name"):
                raise RejectedPlan("Unknown VM identity cannot be deployed.")
            vm_ids.add(vm_id)
        for action in actions:
            if action in ("create", "update", "delete"):
                counts[action] += 1
        if "create" in actions and "delete" in actions:
            counts["replacement"] += 1
    return counts, sorted(vm_ids)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--summary", type=Path, help="Append a safe GitHub job summary.")
    args = parser.parse_args()
    try:
        counts, vm_ids = validate_plan(json.load(sys.stdin))
    except (RejectedPlan, json.JSONDecodeError, TypeError):
        # Do not print exception data: the JSON may contain unredacted secrets.
        print("Plan policy rejected input: require a complete plan with only known VMIDs 250-255 on dell, using bpg/proxmox and no imports.", file=sys.stderr)
        return 1
    summary = (
        "### Terraform plan policy passed\n\n"
        "| Create | Update | Delete | Replacements |\n"
        "| --- | --- | --- | --- |\n"
        f"| {counts['create']} | {counts['update']} | {counts['delete']} | {counts['replacement']} |\n\n"
        f"Checked VMIDs: {', '.join(map(str, vm_ids)) or 'none (data/output changes only)'}.\n"
        "Replacements are included in both create and delete counts.\n"
    )
    print(summary)
    if args.summary:
        with args.summary.open("a") as handle:
            handle.write(summary + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
