"""Reset orchestration: offline validation, disposable PostgreSQL, and actual local HTTP."""

import copy
import io
import os
import sys
import tempfile
import unittest
import uuid
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch

import psycopg
from psycopg import sql
from psycopg.conninfo import make_conninfo
from filelock import FileLock

import test_tink_connector as tc

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import reset_state as cli

USER_ID = "10000000-0000-0000-0000-000000000010"
ANNA_ID = "10000000-0000-0000-0000-000000000001"


class MappingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.mapping = self.directory / "users.json"
        self.value = cli.tink.read_json(cli.DEFAULT_MAPPING)

    def write(self):
        self.mapping.write_text(cli.tink.encode(self.value), encoding="utf-8")

    def test_default_plan_is_offline_and_creates_no_files(self):
        with patch.dict(os.environ, {}, clear=True), redirect_stdout(io.StringIO()) as output:
            self.assertEqual(cli.main(["plan", "--run-id", "preview"]), 0)
        result = cli.tink.json.loads(output.getvalue())
        self.assertEqual(result["users"][0]["fourMoneyUserId"], USER_ID)
        self.assertEqual(len(result["users"]), 1)
        self.assertEqual(list(self.directory.iterdir()), [])

    def test_relative_preset_and_default_preset(self):
        self.write()
        self.assertEqual(cli.load_mapping(self.mapping)[0]["preset"], cli.tink.read_json(tc.cli.PRESETS / "basic-pln.json"))
        preset = tc.cli.read_json(tc.cli.PRESETS / "basic-pln.json")
        (self.directory / "custom.json").write_text(cli.tink.encode(preset))
        self.value["users"][0]["preset"] = "custom.json"
        self.write()
        self.assertEqual(cli.load_mapping(self.mapping)[0]["preset"], preset)
        del self.value["users"][0]["preset"]
        self.write()
        self.assertEqual(cli.load_mapping(self.mapping)[0]["preset"], preset)

    def test_invalid_mapping_rejected_before_environment_or_sql(self):
        original = copy.deepcopy(self.value)
        bad_values = [{"version": 2, "users": []}, {"version": 1, "users": []}]
        for field, value in (("fourMoneyUserId", "bad"), ("key", "../bad"), ("preset", "missing.json")):
            candidate = copy.deepcopy(original)
            candidate["users"][0][field] = value
            bad_values.append(candidate)
        for duplicate in (original["users"][0], {**original["users"][0], "key": "other"},
                          {**original["users"][0], "fourMoneyUserId": ANNA_ID}):
            bad_values.append({"version": 1, "users": [original["users"][0], duplicate]})
        for value in bad_values:
            self.value = value
            self.write()
            with self.subTest(value=value), patch.object(cli, "environment") as environment, redirect_stderr(io.StringIO()):
                self.assertEqual(cli.reset(mapping=str(self.mapping)), 1)
                environment.assert_not_called()


@unittest.skipUnless(os.environ.get("FOURMONEY_TEST_POSTGRES_DSN") and os.environ.get("FOURMONEY_TEST_MIGRATIONS_SQL"),
                     "Set disposable FOURMONEY_TEST_POSTGRES_DSN and FOURMONEY_TEST_MIGRATIONS_SQL (EF migration script)")
class ResetPostgresTests(unittest.TestCase):
    write = MappingTests.write

    def setUp(self):
        MappingTests.setUp(self)
        self.write()
        self.maintenance = os.environ["FOURMONEY_TEST_POSTGRES_DSN"]
        self.database = "reset_tests_" + uuid.uuid4().hex
        with psycopg.connect(self.maintenance, autocommit=True) as connection:
            connection.execute(sql.SQL("CREATE DATABASE {}").format(sql.Identifier(self.database)))
        self.addCleanup(self.drop_database)
        self.dsn = make_conninfo(self.maintenance, dbname=self.database)
        with psycopg.connect(self.dsn, autocommit=True) as connection:
            for name in ("01-schemas.sql", "02-tables.sql"):
                cli.run_sql(connection, (cli.REPO / "database" / "init" / name).read_text())
            cli.run_sql(connection, Path(os.environ["FOURMONEY_TEST_MIGRATIONS_SQL"]).read_text(encoding="utf-8-sig"))
        self.api = tc.Sandbox(self.directory)
        self.api.unique_user_ids = True
        self.addCleanup(self.api.close)
        self.env = patch.dict(os.environ, FOURMONEY_DATABASE_DSN=self.dsn, Tink__ClientId=tc.CLIENT_ID,
                              Tink__ClientSecret=tc.SECRET, Tink__ApiBaseUrl=self.api.base_url)
        self.env.start()
        self.addCleanup(self.env.stop)
        self.runs = self.directory / "runs"

    def drop_database(self):
        with psycopg.connect(self.maintenance, autocommit=True) as connection:
            connection.execute(sql.SQL("DROP DATABASE {} WITH (FORCE)").format(sql.Identifier(self.database)))

    def query(self, statement, params=None):
        with psycopg.connect(self.dsn) as connection:
            cursor = connection.execute(statement, params)
            return cursor.fetchall() if cursor.description else None

    def state(self, run="case"):
        return cli.tink.read_json(self.runs / run / "manifest.json")

    def call(self, command="reset", run="case", ok=True):
        # The existing HTTP stand-in checks the connector's durable checkpoint on ingest.
        original_seed = cli.tink.seed

        def seed(**kwargs):
            self.api.manifest = Path(kwargs["runs_dir"]) / kwargs["run_id"] / "manifest.json"
            return original_seed(**kwargs)

        original_resume = cli.tink.resume

        def resume(**kwargs):
            self.api.manifest = Path(kwargs["manifest"])
            return original_resume(**kwargs)

        with patch.object(cli.tink, "seed", side_effect=seed), patch.object(cli.tink, "resume", side_effect=resume), \
                redirect_stdout(io.StringIO()) as output, redirect_stderr(io.StringIO()) as errors:
            if command == "reset":
                result = cli.main(["reset", "--mapping", str(self.mapping), "--run-id", run,
                                   "--runs-dir", str(self.runs)])
            else:
                result = cli.main(["resume", run, "--runs-dir", str(self.runs)])
        self.assertEqual(result, 0 if ok else 1, errors.getvalue())
        self.assertNotIn(tc.SECRET, output.getvalue() + errors.getvalue())
        return errors.getvalue()

    def test_full_seed_and_repeated_reset_create_fresh_users(self):
        self.call()
        first = self.state()
        self.assertTrue(first["completed"])
        self.assertEqual(self.query('SELECT tink_user_id FROM "user"."user" WHERE _id = %s', (USER_ID,))[0][0],
                         first["users"][0]["tinkUserId"])
        self.assertEqual(self.query('SELECT count(*) FROM "user"."user"')[0][0], 5)
        self.assertEqual(self.query('SELECT count(*) FROM expense.expense')[0][0], 4)
        self.assertEqual(self.query('SELECT count(*) FROM "user"."user" WHERE tink_user_id IS NOT NULL')[0][0], 1)
        self.call(run="second")
        self.assertNotEqual(first["users"][0]["tinkUserId"], self.state("second")["users"][0]["tinkUserId"])
        creates = [request[2]["external_user_id"] for request in self.api.requests if request[1] == "/api/v1/user/create"]
        self.assertEqual(len(set(creates)), 2)

    def test_multiple_mappings_and_resume_preserve_sql_changes(self):
        self.value["users"].append({"fourMoneyUserId": ANNA_ID, "key": "anna"})
        self.write()
        self.call()
        self.assertEqual(self.query('SELECT count(*) FROM "user"."user" WHERE tink_user_id IS NOT NULL')[0][0], 2)
        self.query("UPDATE expense.category SET name = 'kept' WHERE _id = '30000000-0000-0000-0000-000000000001'")
        writes = len([request for request in self.api.requests if request[0] == "POST"])
        self.call("resume")
        self.assertEqual(writes, len([request for request in self.api.requests if request[0] == "POST"]))
        self.assertEqual(self.query("SELECT name FROM expense.category WHERE _id = '30000000-0000-0000-0000-000000000001'")[0][0], "kept")

    def test_failure_and_resume_use_frozen_mapping_without_sql_replay(self):
        self.api.mode = "failed"
        self.call(ok=False)
        self.assertTrue(self.state()["sqlCompleted"])
        self.assertFalse(self.state()["completed"])
        self.query("UPDATE expense.category SET name = 'kept' WHERE _id = '30000000-0000-0000-0000-000000000001'")
        self.mapping.unlink()
        self.api.mode = None
        self.call("resume")
        self.assertTrue(self.state()["completed"])
        self.assertEqual(self.query("SELECT name FROM expense.category WHERE _id = '30000000-0000-0000-0000-000000000001'"), [("kept",)])
        self.assertEqual(len([request for request in self.api.requests if request[1] == "/api/v1/user/create"]), 1)

    def test_binding_commit_without_checkpoint_is_recoverable(self):
        original = cli.save

        def fail(path, state):
            if state["users"][0]["bound"]:
                raise OSError("synthetic crash")
            original(path, state)

        with patch.object(cli, "save", side_effect=fail):
            self.call(ok=False)
        self.assertTrue(self.state()["users"][0]["seedCompleted"])
        self.assertFalse(self.state()["users"][0]["bound"])
        self.call("resume")
        self.assertTrue(self.state()["completed"])
        self.assertEqual(len([request for request in self.api.requests if request[1] == "/api/v1/user/create"]), 1)

    def test_unavailable_users_are_rejected_before_http(self):
        for user_id in ("10000000-0000-0000-0000-000000000004", str(uuid.uuid4())):
            self.value["users"][0]["fourMoneyUserId"] = user_id
            self.write()
            self.call(run=uuid.uuid4().hex, ok=False)
        self.assertEqual(self.api.requests, [])

    def test_sql_error_rolls_back(self):
        sql_path = self.directory / "seed.sql"
        actual = cli.SQL_FILE.read_text()
        sql_path.write_text(actual.replace("COMMIT;", "SELECT 1 / 0;\nCOMMIT;"))
        with patch.object(cli, "SQL_FILE", sql_path):
            self.call(ok=False)
        self.assertEqual(self.query('SELECT count(*) FROM "user"."user"')[0][0], 0)
        self.assertEqual(self.api.requests, [])
        self.assertFalse(self.state()["sqlCompleted"])

    def test_sql_commit_without_checkpoint_can_be_replayed_before_http(self):
        original = cli.save

        def fail(path, state):
            if state["sqlCompleted"]:
                raise OSError("synthetic checkpoint loss")
            original(path, state)

        with patch.object(cli, "save", side_effect=fail):
            self.call(ok=False)
        self.assertFalse(self.state()["sqlCompleted"])
        self.assertEqual(self.api.requests, [])
        self.call("resume")
        self.assertTrue(self.state()["completed"])

    def test_concurrent_database_and_run_locks_reject_reset(self):
        with psycopg.connect(self.dsn, autocommit=True) as connection:
            connection.execute("SELECT pg_advisory_lock(%s)", (cli.DATABASE_LOCK,))
            self.assertIn("reset_database_locked", self.call(ok=False))
        self.assertEqual(self.query('SELECT count(*) FROM "user"."user"')[0][0], 0)
        with FileLock(str(self.runs / "case" / "run.lock")):
            self.assertIn("reset_run_locked", self.call("resume", ok=False))
        self.call("resume")

    def test_environment_mismatch_and_reused_run_rejected(self):
        self.call()
        self.assertIn("reset_run_exists_use_resume", self.call(ok=False))
        with patch.dict(os.environ, Tink__ClientId="other-client"):
            self.assertIn("reset_environment_mismatch", self.call("resume", ok=False))


if __name__ == "__main__":
    unittest.main()
