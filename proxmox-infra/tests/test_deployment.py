import copy
import importlib.util
import io
import json
import os
import subprocess
import tempfile
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


policy = load("policy", "check-plan.py")
head = load("head", "check-current-master.py")


def plan(actions=("create",), vm_id=250):
    value = {"vm_id": vm_id, "node_name": "dell", "initialization": [{"password": "CONFIDENTIAL_CANARY"}]}
    return {
        "format_version": "1.2", "complete": True, "errored": False, "planned_values": {},
        "resource_changes": [{
            "mode": "managed", "type": "proxmox_virtual_environment_vm",
            "provider_name": policy.PROVIDER,
            "change": {
                "actions": list(actions),
                "before": None if actions == ("create",) else copy.deepcopy(value),
                "after": None if actions == ("delete",) else copy.deepcopy(value),
                "after_unknown": {},
            },
        }],
    }


class PlanPolicyTests(unittest.TestCase):
    def test_all_allowed_vm_ids_and_actions(self):
        for vm_id in range(250, 256):
            for actions in policy.ALLOWED_ACTIONS:
                with self.subTest(vm_id=vm_id, actions=actions):
                    counts, checked = policy.validate_plan(plan(actions, vm_id))
                    self.assertEqual(checked, [vm_id])
                    self.assertEqual(counts["replacement"], int(len(actions) == 2))

    def test_before_and_after_must_both_be_in_scope(self):
        for side in ("before", "after"):
            for vm_id in (249, 256, 205, None, True, "250"):
                with self.subTest(side=side, vm_id=vm_id):
                    value = plan(("update",))
                    value["resource_changes"][0]["change"][side]["vm_id"] = vm_id
                    with self.assertRaises(policy.RejectedPlan):
                        policy.validate_plan(value)

    def test_reject_other_nodes_and_unknown_identity(self):
        for field, value in (("node_name", "other-host"), ("node_name", None), ("vm_id", None)):
            document = plan()
            document["resource_changes"][0]["change"]["after"][field] = value
            with self.assertRaises(policy.RejectedPlan):
                policy.validate_plan(document)
        document = plan()
        document["resource_changes"][0]["change"]["after_unknown"] = {"vm_id": True}
        with self.assertRaises(policy.RejectedPlan):
            policy.validate_plan(document)

    def test_reject_wrong_provider_type_mode_and_import(self):
        for field, value in (("provider_name", "untrusted/provider"), ("type", "terraform_data"), ("mode", "unexpected")):
            document = plan()
            document["resource_changes"][0][field] = value
            with self.assertRaises(policy.RejectedPlan):
                policy.validate_plan(document)
        document = plan(("no-op",))
        document["resource_changes"][0]["change"]["importing"] = {"id": "250"}
        with self.assertRaises(policy.RejectedPlan):
            policy.validate_plan(document)

    def test_no_op_resources_are_still_checked(self):
        with self.assertRaises(policy.RejectedPlan):
            policy.validate_plan(plan(("no-op",), 205))

    def test_data_output_and_empty_plans(self):
        document = plan()
        document["resource_changes"] = [{"mode": "data", "type": "proxmox_virtual_environment_nodes"}]
        document["output_changes"] = {"proxmox_nodes": {"actions": ["update"]}}
        self.assertEqual(policy.validate_plan(document)[1], [])
        document.pop("resource_changes")
        self.assertEqual(policy.validate_plan(document)[0]["create"], 0)

    def test_reject_invalid_incomplete_errored_and_unknown_actions(self):
        for document in ({}, [], {"format_version": "2.0", "planned_values": {}}):
            with self.assertRaises(policy.RejectedPlan):
                policy.validate_plan(document)
        for key in ("complete", "errored"):
            document = plan()
            document[key] = key == "errored"
            with self.assertRaises(policy.RejectedPlan):
                policy.validate_plan(document)
        document = plan(("forget",))
        with self.assertRaises(policy.RejectedPlan):
            policy.validate_plan(document)
        for field in ("action_invocations", "deferred_changes"):
            document = plan()
            document[field] = [{}]
            with self.assertRaises(policy.RejectedPlan):
                policy.validate_plan(document)

    def test_cli_does_not_disclose_json_values(self):
        for vm_id in (250, 205):
            result = subprocess.run(["python3", str(ROOT / "scripts/check-plan.py")], input=json.dumps(plan(vm_id=vm_id)), capture_output=True, text=True)
            self.assertEqual(result.returncode, int(vm_id == 205))
            self.assertNotIn("CONFIDENTIAL_CANARY", result.stdout + result.stderr)
        result = subprocess.run(["python3", str(ROOT / "scripts/check-plan.py")], input='{"secret":"CONFIDENTIAL_CANARY"', capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("CONFIDENTIAL_CANARY", result.stdout + result.stderr)


class MasterGuardTests(unittest.TestCase):
    def test_current_and_superseded_commits(self):
        sha = "a" * 40
        for returned, expected in ((sha, True), ("b" * 40, False)):
            response = io.BytesIO(json.dumps({"object": {"type": "commit", "sha": returned}}).encode())
            with patch.object(head.urllib.request, "urlopen", return_value=response) as request:
                self.assertEqual(head.current_master("Drake3001/ZPI-4money", sha, "not-a-real-token"), expected)
                self.assertEqual(
                    request.call_args.args[0].full_url,
                    "https://api.github.com/repos/Drake3001/ZPI-4money/git/ref/heads/master",
                )

    def test_api_failure_and_invalid_response_fail_closed(self):
        with patch.object(head.urllib.request, "urlopen", side_effect=urllib.error.URLError("offline")):
            with self.assertRaises(urllib.error.URLError):
                head.current_master("Drake3001/ZPI-4money", "a" * 40, "not-a-real-token")
        with patch.object(head.urllib.request, "urlopen", return_value=io.BytesIO(b'{"object":{"type":"tag","sha":"invalid"}}')):
            with self.assertRaises(ValueError):
                head.current_master("Drake3001/ZPI-4money", "a" * 40, "not-a-real-token")

    def test_missing_authentication_fails_before_network(self):
        with patch.object(head.urllib.request, "urlopen") as request:
            with self.assertRaises(ValueError):
                head.current_master("Drake3001/ZPI-4money", "a" * 40, "")
            request.assert_not_called()


class PlanStepTests(unittest.TestCase):
    def run_step(self, status, document, show_status=0):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            binary = folder / "terraform"
            binary.write_text(
                '#!/usr/bin/env python3\nimport os, sys\n'
                'if sys.argv[1] == "plan":\n'
                '    raise SystemExit(int(os.environ["FAKE_PLAN_STATUS"]))\n'
                'if sys.argv[1] == "show":\n'
                '    if int(os.environ["FAKE_SHOW_STATUS"]): raise SystemExit(1)\n'
                '    print(os.environ["FAKE_PLAN_JSON"])\n'
            )
            binary.chmod(0o700)
            output = folder / "output"
            summary = folder / "summary"
            environment = {**os.environ, "PATH": str(folder) + os.pathsep + os.environ["PATH"], "TF_PLAN_DIR": directory,
                           "GITHUB_OUTPUT": str(output), "GITHUB_STEP_SUMMARY": str(summary), "FAKE_PLAN_STATUS": str(status),
                           "FAKE_SHOW_STATUS": str(show_status), "FAKE_PLAN_JSON": json.dumps(document)}
            result = subprocess.run(["bash", "scripts/ci-plan.sh"], cwd=ROOT, env=environment, capture_output=True, text=True)
            return result, output.read_text() if output.exists() else "", summary.read_text() if summary.exists() else ""

    def test_no_change_and_changed_exit_codes(self):
        for status, expected in ((0, "false"), (2, "true")):
            result, output, summary = self.run_step(status, plan())
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(output, f"has_changes={expected}\n")
            self.assertNotIn("CONFIDENTIAL_CANARY", summary)

    def test_plan_error_show_error_and_policy_rejection_emit_no_apply_output(self):
        for status, document, show_status in ((1, plan(), 0), (2, plan(), 1), (2, plan(vm_id=205), 0)):
            result, output, _ = self.run_step(status, document, show_status)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(output, "")


if __name__ == "__main__":
    unittest.main()
