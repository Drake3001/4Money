import importlib.util
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

HOOKS = Path(__file__).resolve().parents[1]


def module(name):
    spec = importlib.util.spec_from_file_location(name, HOOKS / (name + ".py"))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


message = module("clickup_message")
formatter = module("dotnet_format")


class ClickUpTests(unittest.TestCase):
    def test_prefix_body_trailers_and_line_endings(self):
        original = "feat: add endpoint\r\n\r\nBody.\r\nSigned-off-by: Example\r\n"
        modified, warning = message.prepare(original, "CU-869fb6e1e/Author")
        self.assertEqual(modified, "CU-869fb6e1e - " + original)
        self.assertIsNone(warning)
        self.assertEqual(message.prepare(modified, "CU-869fb6e1e")[0], modified)

    def test_existing_id_and_no_ticket(self):
        original = "CU-869fb6e1e - description\n"
        self.assertEqual(message.prepare(original, "CU-869fb6e1e/Author")[0], original)
        unchanged, warning = message.prepare("description\n", "configure_runner")
        self.assertEqual(unchanged, "description\n")
        self.assertTrue(warning)

    def test_different_subject_task_is_rejected(self):
        with self.assertRaises(ValueError):
            message.prepare("CU-other - description\n", "CU-869fb6e1e/Author")

    def test_empty_editor_gets_prefix_but_requires_description(self):
        modified, _ = message.prepare("\n# Commit instructions\n", "CU-869fb6e1e/Author")
        self.assertTrue(modified.startswith("CU-869fb6e1e - \n"))
        with self.assertRaises(ValueError):
            message.validate(modified)
        message.validate("CU-869fb6e1e - meaningful description\n")
        message.validate("description without task\n")

    def test_autosquash_subjects_are_preserved(self):
        for prefix in ("fixup! ", "squash! "):
            original = prefix + "CU-original - description\n"
            self.assertEqual(message.prepare(original, "CU-new/Author")[0], original)

    def test_custom_comment_character(self):
        result = subprocess.CompletedProcess([], 0, ";\n", "")
        with patch.object(message, "git", return_value=result):
            modified, _ = message.prepare("; Instructions\ndescription\n", "CU-abc/Author")
        self.assertEqual(modified, "; Instructions\nCU-abc - description\n")


class GitMessageIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.git("init", "-b", "CU-abc/Author")
        self.file = self.root / "message"

    def tearDown(self):
        self.temp.cleanup()

    def git(self, *args):
        return subprocess.run(["git", *args], cwd=self.root, capture_output=True, text=True, check=True)

    def run_hook(self, source="message"):
        env = {**os.environ, "PRE_COMMIT_COMMIT_MSG_SOURCE": source}
        return subprocess.run(["python3", str(HOOKS / "clickup_message.py"), "prepare", str(self.file)], cwd=self.root, env=env, capture_output=True, text=True)

    def test_real_branch_and_conflict_without_changes(self):
        self.file.write_text("description\n")
        self.assertEqual(self.run_hook().returncode, 0)
        self.assertEqual(self.file.read_text(), "CU-abc - description\n")
        self.file.write_text("CU-other - description\n")
        self.assertNotEqual(self.run_hook().returncode, 0)
        self.assertEqual(self.file.read_text(), "CU-other - description\n")

    def test_merge_amend_squash_and_rebase_preserve_message(self):
        for source in ("merge", "commit", "squash"):
            self.file.write_text("retained message\n")
            self.assertEqual(self.run_hook(source).returncode, 0)
            self.assertEqual(self.file.read_text(), "retained message\n")
        (self.root / ".git/rebase-merge").mkdir()
        self.assertEqual(self.run_hook().returncode, 0)
        self.assertEqual(self.file.read_text(), "retained message\n")

    def test_cherry_pick_and_revert_preserve_message(self):
        for marker in ("CHERRY_PICK_HEAD", "REVERT_HEAD"):
            path = self.root / ".git" / marker
            path.write_text("a" * 40)
            self.file.write_text("retained message\n")
            self.assertEqual(self.run_hook().returncode, 0)
            self.assertEqual(self.file.read_text(), "retained message\n")
            path.unlink()

    def test_detached_head_leaves_message_unchanged(self):
        # Synthetic fixture commit only; never commits project code.
        self.git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m", "fixture")
        self.git("checkout", "--detach")
        self.file.write_text("description\n")
        result = self.run_hook()
        self.assertEqual(result.returncode, 0)
        self.assertIn("detached HEAD", result.stderr)
        self.assertEqual(self.file.read_text(), "description\n")


class DotnetTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / "backend").mkdir()

    def tearDown(self):
        self.temp.cleanup()

    def test_flat_nested_missing_and_ambiguous_solutions(self):
        with self.assertRaises(ValueError):
            formatter.find_solution(self.root)
        flat = self.root / "backend/FourM.slnx"
        flat.write_text("<Solution />")
        self.assertEqual(formatter.find_solution(self.root), flat)
        flat.unlink()
        nested = self.root / "backend/ZPI4M.Backend/ZPI4M.Backend.slnx"
        nested.parent.mkdir()
        nested.write_text("<Solution />")
        self.assertEqual(formatter.find_solution(self.root), nested)
        generated = nested.parent / "obj/ignored.slnx"
        generated.parent.mkdir()
        generated.write_text("<Solution />")
        self.assertEqual(formatter.find_solution(self.root), nested)
        flat.write_text("<Solution />")
        with self.assertRaises(ValueError):
            formatter.find_solution(self.root)

    def source(self):
        (self.root / "backend/FourM.slnx").write_text("<Solution />")
        path = self.root / "backend/Source With Spaces.cs"
        path.write_text("class Example {}")
        return path.relative_to(self.root).as_posix()

    def test_no_files_skips_sdk_and_missing_sdk_fails(self):
        with patch.object(formatter.shutil, "which", return_value=None) as which:
            self.assertEqual(formatter.format_files(self.root, []), 0)
            which.assert_not_called()
            with self.assertRaises(ValueError):
                formatter.format_files(self.root, [self.source()])

    def test_only_supplied_files_no_restore_no_build_or_staging(self):
        source = self.source()
        with patch.object(formatter.shutil, "which", return_value="dotnet"), patch.object(formatter.subprocess, "run") as run:
            run.side_effect = [subprocess.CompletedProcess([], 0, "10.0.111\n", ""), subprocess.CompletedProcess([], 0), subprocess.CompletedProcess([], 0)]
            self.assertEqual(formatter.format_files(self.root, [source]), 0)
            self.assertEqual(len(run.call_args_list), 3)
            for call in run.call_args_list[1:]:
                args = call.args[0]
                self.assertIn("--no-restore", args)
                self.assertIn("./Source With Spaces.cs", args)
                self.assertNotIn("build", args)
                self.assertNotIn("git", args)

    def test_restore_failure_stops_formatting(self):
        source = self.source()
        with patch.object(formatter.shutil, "which", return_value="dotnet"), patch.object(formatter.subprocess, "run") as run:
            run.side_effect = [subprocess.CompletedProcess([], 0, "10.0.111\n", ""), subprocess.CompletedProcess([], 2)]
            self.assertEqual(formatter.format_files(self.root, [source]), 2)
            self.assertEqual(len(run.call_args_list), 2)

    def test_source_outside_backend_is_rejected(self):
        with self.assertRaises(ValueError):
            formatter.format_files(self.root, ["elsewhere.cs"])


class InstallerTests(unittest.TestCase):
    def test_existing_native_hook_and_hooks_path_are_not_overwritten(self):
        for custom_path in (False, True):
            with tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                subprocess.run(["git", "init", "-q", str(root)], check=True)
                hook = root / ".git/hooks/pre-commit"
                hook.write_text("#!/bin/sh\necho custom\n")
                if custom_path:
                    subprocess.run(["git", "config", "core.hooksPath", "custom-hooks"], cwd=root, check=True)
                result = subprocess.run(["python3", str(HOOKS / "install.py")], cwd=root, capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(hook.read_text(), "#!/bin/sh\necho custom\n")


if __name__ == "__main__":
    unittest.main()
