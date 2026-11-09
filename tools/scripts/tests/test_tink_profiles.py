"""Profile batches against existing PostgreSQL records and the stateful HTTP Tink stand-in."""

import copy
import io
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch

import psycopg
from filelock import FileLock

import test_tink_connector as tc
import test_tink_connector_binding as binding

USER_ID, OTHER_ID = binding.USER_ID, binding.OTHER_ID

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import tink_profiles as cli
import tink_batch


class ProfileMappingTests(unittest.TestCase):
    def test_default_plan_is_offline_and_has_no_sql(self):
        with patch.dict(os.environ, {}, clear=True), redirect_stdout(io.StringIO()) as output:
            self.assertEqual(cli.main(["plan", "--run-id", "preview"]), 0)
        result = cli.tink.json.loads(output.getvalue())
        self.assertEqual(result["users"][0]["fourMoneyUserId"], USER_ID)
        self.assertNotIn("sql", result)

    def test_custom_mapping_relative_preset_and_validation_before_environment(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            mapping = directory / "users.json"
            preset = tc.cli.read_json(tc.cli.PRESETS / "basic-pln.json")
            tc.cli.atomic_write(directory / "custom.json", preset)
            value = {"version": 1, "users": [{"fourMoneyUserId": USER_ID, "key": "one", "preset": "custom.json"}]}
            tc.cli.atomic_write(mapping, value)
            before = sorted(directory.iterdir())
            with patch.object(cli, "environment") as environment, redirect_stdout(io.StringIO()):
                self.assertEqual(cli.plan(mapping=str(mapping)), 0)
                environment.assert_not_called()
            self.assertEqual(sorted(directory.iterdir()), before)
            self.assertEqual(cli.load_mapping(mapping)[0]["preset"], preset)
            bad = [{"version": 2, "users": []}, {"version": 1, "users": []}]
            for field, changed in (("fourMoneyUserId", "bad"), ("key", "../bad"), ("preset", "missing.json")):
                candidate = copy.deepcopy(value)
                candidate["users"][0][field] = changed
                bad.append(candidate)
            for duplicate in (value["users"][0], {**value["users"][0], "key": "two"},
                              {**value["users"][0], "fourMoneyUserId": OTHER_ID}):
                bad.append({"version": 1, "users": [value["users"][0], duplicate]})
            for candidate in bad:
                tc.cli.atomic_write(mapping, candidate)
                with self.subTest(candidate=candidate), patch.object(cli, "environment") as environment, redirect_stderr(io.StringIO()):
                    self.assertEqual(cli.apply(mapping=str(mapping)), 1)
                    environment.assert_not_called()


@unittest.skipUnless(os.environ.get("FOURMONEY_TEST_POSTGRES_DSN"), "Set disposable FOURMONEY_TEST_POSTGRES_DSN")
class ProfilePostgresTests(unittest.TestCase):
    drop_database = binding.BindingPostgresTests.drop_database
    query = binding.BindingPostgresTests.query
    mapping = binding.BindingPostgresTests.mapping

    def setUp(self):
        binding.BindingPostgresTests.setUp(self)
        self.api.unique_user_ids = True
        self.value = {"version": 1, "users": [{"fourMoneyUserId": USER_ID, "key": "one"}]}
        self.mapping_file = self.directory / "users.json"
        self.write()
        self.runs = self.directory / "runs"
        env = patch.dict(os.environ, Tink__ClientId=tc.CLIENT_ID, Tink__ClientSecret=tc.SECRET,
                         Tink__ApiBaseUrl=self.api.base_url)
        env.start()
        self.addCleanup(env.stop)
        self.query('ALTER TABLE "user"."user" ADD COLUMN name text DEFAULT \'preserved\'')
        self.query('UPDATE "user"."user" SET tink_user_id = %s WHERE _id = %s', ("old-owner", USER_ID))
        self.query('UPDATE "user"."user" SET tink_user_id = %s WHERE _id = %s', ("old-other", OTHER_ID))
        self.query('CREATE TABLE sentinel (id integer PRIMARY KEY, data text)')
        self.query("INSERT INTO sentinel VALUES (1, 'expense/connection/session data stays intact')")
        self.before_users = self.query('SELECT to_jsonb(u) - \'tink_user_id\' FROM "user"."user" u ORDER BY _id')
        self.before_sentinel = self.query('SELECT * FROM sentinel')

    def write(self):
        cli.tink.atomic_write(self.mapping_file, self.value)

    def state(self, run="case"):
        return cli.tink.read_json(self.runs / run / "manifest.json")

    def call(self, command="apply", run="case", ok=True):
        original_seed, original_resume = cli.tink.seed, cli.tink.resume

        def seed(**kwargs):
            self.api.manifest = Path(kwargs["runs_dir"]) / kwargs["run_id"] / "manifest.json"
            return original_seed(**kwargs)

        def resume(**kwargs):
            self.api.manifest = Path(kwargs["manifest"])
            return original_resume(**kwargs)

        with patch.object(cli.tink, "seed", side_effect=seed), patch.object(cli.tink, "resume", side_effect=resume), \
                redirect_stdout(io.StringIO()) as output, redirect_stderr(io.StringIO()) as errors:
            args = (["apply", "--mapping", str(self.mapping_file), "--run-id", run]
                    if command == "apply" else ["resume", run])
            result = cli.main([*args, "--runs-dir", str(self.runs)])
        self.assertEqual(result, 0 if ok else 1, errors.getvalue())
        for secret in (tc.SECRET, tc.TOKEN, tc.CODE, self.dsn):
            self.assertNotIn(secret, output.getvalue() + errors.getvalue())
            for manifest in self.runs.rglob("*.json"):
                self.assertNotIn(secret, manifest.read_text())
        return errors.getvalue()

    def writes(self, path):
        return [r for r in self.api.requests if r[0] == "POST" and r[1] == path]

    def assert_preserved(self):
        self.assertEqual(self.query('SELECT to_jsonb(u) - \'tink_user_id\' FROM "user"."user" u ORDER BY _id'), self.before_users)
        self.assertEqual(self.query('SELECT * FROM sentinel'), self.before_sentinel)

    def test_apply_replaces_only_selected_mapping_and_new_batch_creates_fresh_identity(self):
        self.call()
        first = self.mapping()
        self.assertNotEqual(first, "old-owner")
        self.assertEqual(self.query('SELECT tink_user_id FROM "user"."user" WHERE _id = %s', (OTHER_ID,)), [("old-other",)])
        self.assert_preserved()
        self.call(run="second")
        self.assertNotEqual(self.mapping(), first)
        self.assertEqual(len(self.writes('/api/v1/user/create')), 2)
        self.assert_preserved()

    def test_multiple_profiles_and_completed_resume_do_not_repeat_writes(self):
        self.value["users"].append({"fourMoneyUserId": OTHER_ID, "key": "two"})
        self.write()
        self.call()
        self.assertEqual(len({entry['tinkUserId'] for entry in self.state()['users']}), 2)
        writes = len([r for r in self.api.requests if r[0] == 'POST'])
        self.mapping_file.unlink()
        self.call('resume')
        self.assertEqual(len([r for r in self.api.requests if r[0] == 'POST']), writes)
        self.assert_preserved()

    def test_all_targets_preflight_before_remote_writes(self):
        self.value['users'].append({'fourMoneyUserId': OTHER_ID, 'key': 'two'})
        self.write()
        for unavailable in ('DELETE FROM "user"."user" WHERE _id = %s',
                            'UPDATE "user"."user" SET is_active = false WHERE _id = %s',
                            'UPDATE "user"."user" SET is_email_confirmed = false WHERE _id = %s'):
            # Restore the second profile between preflight cases.
            self.query('INSERT INTO "user"."user" (_id,is_active,is_email_confirmed) VALUES (%s,true,true) ON CONFLICT (_id) DO UPDATE SET is_active=true,is_email_confirmed=true', (OTHER_ID,))
            self.query(unavailable, (OTHER_ID,))
            self.call(run=cli.uuid.uuid4().hex, ok=False)
        self.assertEqual(self.api.requests, [])
        self.assertEqual(self.mapping(), 'old-owner')

    def test_failed_ingest_keeps_old_mapping_and_resume_uses_frozen_inputs(self):
        self.api.mode = 'failed'
        self.call(ok=False)
        self.assertEqual(self.mapping(), 'old-owner')
        self.mapping_file.unlink()
        self.api.mode = None
        self.call('resume')
        self.assertTrue(self.state()['completed'])
        self.assertEqual(len(self.writes('/api/v1/user/create')), 1)
        self.assert_preserved()

    def test_partial_batch_stops_then_resumes_remaining_profile(self):
        self.value['users'].append({'fourMoneyUserId': OTHER_ID, 'key': 'two'})
        self.write()
        original = cli.tink.link_db

        def bind(**kwargs):
            result = original(**kwargs)
            self.api.mode = 'failed'
            return result

        with patch.object(cli.tink, 'link_db', side_effect=bind):
            self.call(ok=False)
        state = self.state()
        self.assertTrue(state['users'][0]['bound'])
        self.assertFalse(state['users'][1]['bound'])
        first = self.mapping()
        self.assertEqual(self.query('SELECT tink_user_id FROM "user"."user" WHERE _id = %s', (OTHER_ID,)), [('old-other',)])
        self.api.mode = None
        self.call('resume')
        self.assertEqual(self.mapping(), first)
        self.assertEqual(len(self.writes('/api/v1/user/create')), 2)
        self.assert_preserved()

    def test_binding_commit_before_checkpoint_is_recoverable(self):
        original = cli.save

        def fail(path, state):
            if state['users'][0]['bound']:
                raise OSError('synthetic checkpoint loss')
            original(path, state)

        with patch.object(cli, 'save', side_effect=fail):
            self.call(ok=False)
        self.assertFalse(self.state()['users'][0]['bound'])
        first = self.mapping()
        self.call('resume')
        self.assertEqual(self.mapping(), first)
        self.assertEqual(len(self.writes('/api/v1/user/create')), 1)

    def test_uniqueness_conflict_keeps_old_mapping_and_can_resume(self):
        self.api.unique_user_ids = False
        self.query('UPDATE "user"."user" SET tink_user_id = %s WHERE _id = %s', ('tink-user-1', OTHER_ID))
        self.assertIn('local_mapping_conflict', self.call(ok=False))
        self.assertEqual(self.mapping(), 'old-owner')
        self.query('UPDATE "user"."user" SET tink_user_id = NULL WHERE _id = %s', (OTHER_ID,))
        self.call('resume')
        self.assertEqual(self.mapping(), 'tink-user-1')
        self.assertEqual(len(self.writes('/data/v2/connector/transactions')), 1)

    def test_deactivation_before_binding_preserves_mapping(self):
        original = cli.tink.link_db

        def deactivate(**kwargs):
            self.query('UPDATE "user"."user" SET is_active = false WHERE _id = %s', (USER_ID,))
            return original(**kwargs)

        with patch.object(cli.tink, 'link_db', side_effect=deactivate):
            self.assertIn('local_user_inactive', self.call(ok=False))
        self.assertEqual(self.mapping(), 'old-owner')

    def test_database_and_run_locks_are_shared_with_reset(self):
        with psycopg.connect(self.dsn, autocommit=True) as connection:
            connection.execute('SELECT pg_advisory_lock(%s)', (tink_batch.DATABASE_LOCK,))
            self.assertIn('profiles_database_locked', self.call(ok=False))
        self.assertEqual(self.api.requests, [])
        with FileLock(str(self.runs / 'case' / 'run.lock')):
            self.assertIn('profiles_run_locked', self.call('resume', ok=False))
        self.call('resume')

    def test_changed_completed_mapping_refuses_resume(self):
        self.call()
        self.query('UPDATE "user"."user" SET tink_user_id = %s WHERE _id = %s', ('changed-by-other', USER_ID))
        self.api.requests.clear()
        self.assertIn('profiles_binding_changed', self.call('resume', ok=False))
        self.assertEqual(self.api.requests, [])

    def test_environment_mismatch_and_reused_run_are_rejected(self):
        self.call()
        self.assertIn('profiles_run_exists_use_resume', self.call(ok=False))
        for variable, value in (('Tink__ClientId', 'other-client'), ('Tink__ApiBaseUrl', 'http://localhost:1')):
            with patch.dict(os.environ, **{variable: value}):
                self.assertIn('profiles_environment_mismatch', self.call('resume', ok=False))
        with patch.object(cli, 'environment', return_value=(cli.tink.LocalDatabase(), 'other-database', self.api.base_url, self.state()['clientFingerprint'])):
            self.assertIn('profiles_environment_mismatch', self.call('resume', ok=False))

    def test_mismatched_child_manifest_is_rejected_before_network(self):
        self.api.mode = 'failed'
        self.call(ok=False)
        child = cli.tink.read_json(self.api.manifest)
        child['runId'] = 'different-run'
        for invalid in (child, []):
            cli.tink.atomic_write(self.api.manifest, invalid)
            self.api.requests.clear()
            self.assertIn('profiles_connector_manifest_mismatch', self.call('resume', ok=False))
            self.assertEqual(self.api.requests, [])


if __name__ == '__main__':
    unittest.main()
