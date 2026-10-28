"""Identity binding: real CLI/HTTP and disposable PostgreSQL, no real Tink calls."""

import io
import os
import tempfile
import unittest
import uuid
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch

import psycopg
from psycopg import sql
from psycopg.conninfo import make_conninfo

import test_tink_connector as tc

USER_ID = "10000000-0000-0000-0000-000000000010"
OTHER_ID = "10000000-0000-0000-0000-000000000011"


class BindingValidationTests(unittest.TestCase):
    def test_linked_plan_is_offline_and_rejects_ambiguous_identity(self):
        with tempfile.TemporaryDirectory() as directory, redirect_stdout(io.StringIO()) as output:
            result = tc.cli.plan(run_id="linked", runs_dir=directory, four_money_user_id=USER_ID)
            self.assertEqual(result, 0)
            self.assertEqual(tc.json.loads(output.getvalue())["externalUserId"], "4money-" + uuid.UUID(USER_ID).hex)
            self.assertFalse(list(Path(directory).iterdir()))
        preset = tc.cli.read_json(tc.cli.PRESETS / "basic-pln.json")
        with self.assertRaises(tc.cli.ConnectorError):
            tc.cli.create_state(preset, "linked", tc.date.today(), "https://api.tink.com", tc.CLIENT_ID,
                                "4money-test-existing", USER_ID)

    def test_remote_database_is_rejected_before_connecting(self):
        with patch.dict(os.environ, FOURMONEY_DATABASE_DSN="host=remote.example dbname=example"):
            with self.assertRaisesRegex(tc.cli.ConnectorError, "local_database_required"):
                tc.cli.LocalDatabase()


@unittest.skipUnless(os.environ.get("FOURMONEY_TEST_POSTGRES_DSN"), "Set FOURMONEY_TEST_POSTGRES_DSN to a local disposable server")
class BindingPostgresTests(unittest.TestCase):
    call = tc.ConnectorTests.call
    state = tc.ConnectorTests.state
    writes = tc.ConnectorTests.writes

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.api = tc.Sandbox(self.directory)
        self.addCleanup(self.api.close)
        self.maintenance = os.environ["FOURMONEY_TEST_POSTGRES_DSN"]
        self.database = "binding_tests_" + uuid.uuid4().hex
        with psycopg.connect(self.maintenance, autocommit=True) as admin:
            admin.execute(sql.SQL("CREATE DATABASE {}").format(sql.Identifier(self.database)))
        self.addCleanup(self.drop_database)
        self.dsn = make_conninfo(self.maintenance, dbname=self.database)
        with psycopg.connect(self.dsn) as connection:
            connection.execute('CREATE SCHEMA "user"')
            connection.execute('''CREATE TABLE "user"."user" (
                _id uuid PRIMARY KEY, is_active boolean NOT NULL, is_email_confirmed boolean NOT NULL,
                tink_user_id text UNIQUE)''')
            connection.execute('INSERT INTO "user"."user" VALUES (%s, true, true, NULL), (%s, true, true, NULL)',
                               (USER_ID, OTHER_ID))
        env = patch.dict(os.environ, FOURMONEY_DATABASE_DSN=self.dsn)
        env.start()
        self.addCleanup(env.stop)

    def drop_database(self):
        with psycopg.connect(self.maintenance, autocommit=True) as admin:
            admin.execute(sql.SQL("DROP DATABASE {} WITH (FORCE)").format(sql.Identifier(self.database)))

    def query(self, statement, parameters=()):
        with psycopg.connect(self.dsn) as connection:
            cursor = connection.execute(statement, parameters)
            return cursor.fetchall() if cursor.description else None

    def mapping(self):
        return self.query('SELECT tink_user_id FROM "user"."user" WHERE _id = %s', (USER_ID,))[0][0]

    def test_seed_binds_canonical_identity_and_verify_needs_no_database(self):
        self.call("seed", "--four-money-user-id", USER_ID)
        self.assertEqual(self.mapping(), "tink-user-1")
        self.assertEqual(self.state()["localBinding"]["status"], "bound")
        self.assertEqual(self.api.user["externalUserId"], "4money-" + uuid.UUID(USER_ID).hex)
        with patch.dict(os.environ, FOURMONEY_DATABASE_DSN=""):
            self.call("verify")

    def test_legacy_manifest_can_be_linked_repeatedly_and_after_reset(self):
        self.call("seed")
        self.assertNotIn("localBinding", self.state())
        self.call("link-db", "--four-money-user-id", USER_ID)
        self.call("link-db", "--four-money-user-id", USER_ID)
        self.query('UPDATE "user"."user" SET tink_user_id = NULL WHERE _id = %s', (USER_ID,))
        self.call("link-db", "--four-money-user-id", USER_ID)
        self.assertEqual(self.mapping(), "tink-user-1")
        for path in ("/api/v1/user/create", "/data/v2/connector/accounts", "/data/v2/connector/transactions"):
            self.assertEqual(len(self.writes(path)), 1)
        self.call("link-db", "--four-money-user-id", OTHER_ID, ok=False)

    def test_preflight_rejects_unavailable_users_before_network(self):
        for active, confirmed, target in [(False, True, USER_ID), (True, False, USER_ID),
                                          (True, True, str(uuid.uuid4()))]:
            self.query('UPDATE "user"."user" SET is_active = %s, is_email_confirmed = %s WHERE _id = %s',
                       (active, confirmed, USER_ID))
            self.call("seed", "--four-money-user-id", target, ok=False)
            self.assertFalse(self.api.requests)
            self.api.manifest.unlink()

    def test_other_owner_conflict_preserves_remote_run_for_resume(self):
        self.query('UPDATE "user"."user" SET tink_user_id = %s WHERE _id = %s', ("tink-user-1", OTHER_ID))
        result = self.call("seed", "--four-money-user-id", USER_ID, ok=False)
        self.assertIn("local_mapping_conflict", result.stderr)
        self.assertEqual(self.state()["stage"], "completed")
        self.assertEqual(self.state()["localBinding"]["status"], "failed")
        self.assertIsNone(self.mapping())
        self.query('UPDATE "user"."user" SET tink_user_id = NULL WHERE _id = %s', (OTHER_ID,))
        self.call("resume")
        self.assertEqual(self.mapping(), "tink-user-1")
        self.assertEqual(len(self.writes("/data/v2/connector/transactions")), 1)

    def test_seed_replaces_existing_mapping_after_success(self):
        self.query('UPDATE "user"."user" SET tink_user_id = %s WHERE _id = %s', ("other-tink-user", USER_ID))
        self.call("seed", "--four-money-user-id", USER_ID)
        self.assertEqual(self.mapping(), "tink-user-1")
        self.assertEqual(self.state()["localBinding"]["status"], "bound")
        self.assertEqual(len(self.writes("/data/v2/connector/accounts")), 1)

    def test_link_db_replaces_existing_mapping_without_remote_writes(self):
        self.call("seed")
        self.query('UPDATE "user"."user" SET tink_user_id = %s WHERE _id = %s', ("other-tink-user", USER_ID))
        self.call("link-db", "--four-money-user-id", USER_ID)
        self.call("link-db", "--four-money-user-id", USER_ID)
        self.assertEqual(self.mapping(), "tink-user-1")
        for path in ("/api/v1/user/create", "/data/v2/connector/accounts", "/data/v2/connector/transactions"):
            self.assertEqual(len(self.writes(path)), 1)

    def test_failed_seed_preserves_existing_mapping_until_successful_resume(self):
        self.query('UPDATE "user"."user" SET tink_user_id = %s WHERE _id = %s', ("other-tink-user", USER_ID))
        self.api.mode = "slow_accounts"
        self.call("seed", "--four-money-user-id", USER_ID, ok=False)
        self.assertEqual(self.mapping(), "other-tink-user")
        self.api.mode = "normal"
        self.call("resume")
        self.assertEqual(self.mapping(), "tink-user-1")
        self.assertEqual(self.state()["localBinding"]["status"], "bound")
        self.assertEqual(len(self.writes("/data/v2/connector/accounts")), 1)

    def test_identity_mismatch_does_not_bind_legacy_run(self):
        self.call("seed")
        self.api.user["id"] = "different-remote-user"
        self.call("link-db", "--four-money-user-id", USER_ID, ok=False)
        self.assertIsNone(self.mapping())

    def test_database_failure_is_safe_and_retryable(self):
        self.call("seed")
        with patch.dict(os.environ, FOURMONEY_DATABASE_DSN=make_conninfo(self.dsn, dbname="missing_" + uuid.uuid4().hex)):
            result = self.call("link-db", "--four-money-user-id", USER_ID, ok=False)
        self.assertIn("local_database_unavailable", result.stderr)
        self.assertNotIn(self.dsn, result.stdout + result.stderr)
        self.call("link-db", "--four-money-user-id", USER_ID)
        self.assertEqual(self.mapping(), "tink-user-1")

    def test_lost_manifest_checkpoint_after_db_commit_is_idempotent(self):
        self.call("seed")
        save = tc.cli.RunStore.save

        def fail_after_commit(store):
            if store.state.get("localBinding", {}).get("status") == "bound":
                raise tc.cli.ConnectorError("manifest_write_failed")
            return save(store)

        with patch.object(tc.cli.RunStore, "save", fail_after_commit), \
                patch.dict(os.environ, Tink__ClientId=tc.CLIENT_ID, Tink__ClientSecret=tc.SECRET), \
                redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            result = tc.cli.link_db(manifest=str(self.api.manifest), four_money_user_id=USER_ID,
                                    base_url=self.api.base_url)
        self.assertEqual(result, 1)
        self.assertEqual(self.mapping(), "tink-user-1")
        self.assertEqual(self.state()["localBinding"]["status"], "binding")
        self.call("link-db", "--four-money-user-id", USER_ID)
        self.assertEqual(self.state()["localBinding"]["status"], "bound")
        self.assertEqual(len(self.writes("/api/v1/user/create")), 1)

    def test_final_binding_rechecks_deactivation(self):
        self.call("seed")
        self.query('UPDATE "user"."user" SET is_active = false WHERE _id = %s', (USER_ID,))
        database = tc.cli.LocalDatabase()
        with self.assertRaisesRegex(tc.cli.ConnectorError, "local_user_inactive"):
            database.bind(USER_ID, self.state()["tinkUserId"])
        self.assertIsNone(self.mapping())
