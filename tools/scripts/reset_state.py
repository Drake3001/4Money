"""Reset local SQL fixtures, create fresh Tink fixtures, and bind their identities."""

import hashlib
import os
import sys
import uuid
from contextlib import contextmanager
from datetime import date, datetime, timezone
from pathlib import Path

import fire
from filelock import FileLock, Timeout

import tink_connector as tink

REPO = Path(__file__).resolve().parents[2]
DEFAULT_MAPPING = Path(__file__).resolve().parent / "reset_state" / "users.json"
DEFAULT_RUNS = REPO / ".cache" / "reset-state"
SQL_FILE = REPO / "database" / "seed_local.sql"
DATABASE_LOCK = int.from_bytes(hashlib.sha256(b"4money/reset-state").digest()[:8], "big", signed=True)


def load_mapping(path):
    path = Path(path).resolve()
    mapping = tink.read_json(path)
    tink.require(isinstance(mapping, dict) and set(mapping) == {"version", "users"}
                 and type(mapping["version"]) is int and mapping["version"] == 1, "invalid_mapping_version")
    tink.require(isinstance(mapping["users"], list) and bool(mapping["users"]), "empty_mapping")
    users, keys, ids = [], set(), set()
    for entry in mapping["users"]:
        tink.require(isinstance(entry, dict) and {"fourMoneyUserId", "key"} <= set(entry)
                     and set(entry) <= {"fourMoneyUserId", "key", "preset"}, "invalid_mapping_entry")
        user_id = tink.local_user_id(entry["fourMoneyUserId"])
        key = entry["key"]
        tink.require(isinstance(key, str) and tink.KEY.fullmatch(key), "invalid_mapping_key")
        tink.require(key not in keys and user_id not in ids, "duplicate_mapping")
        source = entry.get("preset", "basic-pln")
        tink.require(tink.text(source), "invalid_mapping_preset")
        preset_path = tink.PRESETS / "basic-pln.json" if source == "basic-pln" else path.parent / source
        preset = tink.validate_preset(tink.read_json(preset_path))
        users.append({"fourMoneyUserId": user_id, "key": key, "preset": preset})
        keys.add(key)
        ids.add(user_id)
    return users


def environment():
    # Reuse the connector's DSN validation; never include the DSN in persisted state.
    database = tink.LocalDatabase()
    tink.require(tink.text(os.environ.get("Tink__ClientId"))
                 and tink.text(os.environ.get("Tink__ClientSecret")), "missing_Tink__ClientId_or_Tink__ClientSecret")
    base_url = os.environ.get("Tink__ApiBaseUrl", "https://api.tink.com").rstrip("/")
    with database.connection() as connection:
        # Actual libpq settings include defaults from the environment, without secrets.
        target = {name: getattr(connection.info, name) for name in ("host", "port", "dbname", "user")}
    return database, tink.digest(target), base_url, hashlib.sha256(os.environ["Tink__ClientId"].encode()).hexdigest()


@contextmanager
def locked_database(database):
    try:
        with database.psycopg.connect(database.dsn, autocommit=True, connect_timeout=5) as connection:
            connection.execute("SET lock_timeout = '5s'")
            connection.execute("SET statement_timeout = '30s'")
            tink.require(connection.execute("SELECT pg_try_advisory_lock(%s)", (DATABASE_LOCK,)).fetchone()[0],
                         "reset_database_locked")
            yield connection
            # Closing this dedicated session also releases the advisory lock on failures.
    except database.psycopg.Error:
        raise tink.ConnectorError("reset_database_error") from None


def run_sql(connection, sql):
    # The existing file owns BEGIN/COMMIT. Simple protocol preserves its transaction.
    cursor = connection.execute(sql, prepare=False)
    while cursor.nextset():
        pass


def save(path, state):
    state["updatedAt"] = tink.utc_now()
    tink.atomic_write(path, state)


def validate_configuration(users, run_id, as_of, base_url=None):
    if base_url is not None:
        parsed = tink.urlsplit(base_url)
        tink.require(bool(parsed.netloc) and not parsed.username and not parsed.password
                     and not parsed.query and not parsed.fragment, "invalid_base_url")
        tink.require(parsed.scheme == "https" or (parsed.scheme == "http"
                     and parsed.hostname in {"127.0.0.1", "localhost", "::1"}), "https_required")
    for entry in users:
        tink.materialize(tink.validate_preset(entry["preset"]), run_id, as_of)


def validate_state(state, run_id):
    tink.require(isinstance(state, dict) and state.get("version") == 1 and state.get("runId") == run_id,
                 "invalid_reset_manifest")
    tink.require(type(state.get("sqlCompleted")) is bool and type(state.get("completed")) is bool
                 and isinstance(state.get("users"), list) and bool(state["users"]), "invalid_reset_manifest")
    ids, keys, runs = set(), set(), set()
    for entry in state["users"]:
        tink.require(isinstance(entry, dict), "invalid_reset_manifest")
        user_id = tink.local_user_id(entry["fourMoneyUserId"])
        for key in ("key", "connectorRunId"):
            tink.require(isinstance(entry[key], str) and tink.KEY.fullmatch(entry[key]), "invalid_reset_manifest")
        tink.require(user_id not in ids and entry["key"] not in keys and entry["connectorRunId"] not in runs,
                     "invalid_reset_manifest")
        ids.add(user_id)
        keys.add(entry["key"])
        runs.add(entry["connectorRunId"])
        tink.require(all(type(entry.get(key)) is bool for key in ("started", "seedCompleted", "bound"))
                     and (not entry["started"] or state["sqlCompleted"])
                     and (not entry["seedCompleted"] or (entry["started"] and tink.identifier(entry.get("tinkUserId"))))
                     and (not entry["bound"] or entry["seedCompleted"]), "invalid_reset_manifest")
    validate_configuration(state["users"], run_id, date.fromisoformat(state["asOfDate"]), state["baseUrl"])


def continue_run(path, state, database, sql):
    with locked_database(database) as connection:
        if not state["sqlCompleted"]:
            tink.require(all(not entry["started"] for entry in state["users"]), "unsafe_sql_replay")
            tink.require(hashlib.sha256(sql.encode()).hexdigest() == state["sqlHash"], "seed_sql_changed")
            run_sql(connection, sql)
            state["sqlCompleted"] = True
            save(path, state)
        # Validate every target before creating any remote resources.
        for entry in state["users"]:
            tink.LocalDatabase.profile(connection, entry["fourMoneyUserId"])
        for entry in state["users"]:
            manifest = path.parent / "connector" / entry["connectorRunId"] / "manifest.json"
            if entry["bound"]:
                current = tink.LocalDatabase.profile(connection, entry["fourMoneyUserId"])
                tink.require(current == entry["tinkUserId"], "reset_binding_changed")
                continue
            entry["started"] = True
            save(path, state)
            if not entry["seedCompleted"]:
                if manifest.exists():
                    result = tink.resume(manifest=str(manifest), base_url=state["baseUrl"])
                else:
                    preset_path = path.parent / (entry["key"] + ".preset.json")
                    tink.atomic_write(preset_path, entry["preset"])
                    result = tink.seed(preset=str(preset_path), run_id=entry["connectorRunId"],
                                       as_of_date=state["asOfDate"], runs_dir=str(path.parent / "connector"),
                                       base_url=state["baseUrl"])
                tink.require(result == 0, "reset_connector_failed")
                entry["seedCompleted"] = True
                entry["tinkUserId"] = tink.read_json(manifest)["tinkUserId"]
                save(path, state)
            result = tink.link_db(manifest=str(manifest), four_money_user_id=entry["fourMoneyUserId"],
                                  base_url=state["baseUrl"])
            tink.require(result == 0, "reset_binding_failed")
            entry["bound"] = True
            save(path, state)
        state["completed"] = True
        save(path, state)


def execute(command, *, mapping=DEFAULT_MAPPING, run_id=None, as_of_date=None, run=None, runs_dir=DEFAULT_RUNS):
    path = None
    try:
        if command in {"plan", "reset"}:
            users = load_mapping(mapping)
            run_id = run_id or datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + uuid.uuid4().hex[:8]
            tink.require(isinstance(run_id, str) and tink.KEY.fullmatch(run_id), "invalid_run_id")
            as_of = date.fromisoformat(str(as_of_date)) if as_of_date is not None else datetime.now(timezone.utc).date()
            validate_configuration(users, run_id, as_of)
            if command == "plan":
                print(tink.encode({"runId": run_id, "sql": str(SQL_FILE), "asOfDate": as_of.isoformat(),
                                   "users": [{"fourMoneyUserId": entry["fourMoneyUserId"], "key": entry["key"],
                                              "preset": entry["preset"]["name"]} for entry in users]}))
                return 0
        else:
            tink.require(isinstance(run, str) and tink.KEY.fullmatch(run), "invalid_run_id")
            run_id = run
        database, target, base_url, client = environment()
        sql = SQL_FILE.read_text(encoding="utf-8")
        directory = Path(runs_dir).resolve() / run_id
        if command == "reset":
            directory.mkdir(parents=True, exist_ok=True)
        tink.require(directory.is_dir(), "reset_manifest_not_found")
        with FileLock(str(directory / "run.lock"), timeout=0):
            path = directory / "manifest.json"
            if command == "reset":
                tink.require(not path.exists(), "reset_run_exists_use_resume")
                validate_configuration(users, run_id, as_of, base_url)
                for entry in users:
                    entry.update(connectorRunId="reset-" + uuid.uuid4().hex, started=False,
                                 seedCompleted=False, bound=False, tinkUserId=None)
                state = {"version": 1, "runId": run_id, "asOfDate": as_of.isoformat(), "users": users,
                         "sqlHash": hashlib.sha256(sql.encode()).hexdigest(), "sqlCompleted": False,
                         "databaseFingerprint": target, "baseUrl": base_url, "clientFingerprint": client,
                         "completed": False}
                save(path, state)
            else:
                state = tink.read_json(path)
                validate_state(state, run_id)
                tink.require(state["databaseFingerprint"] == target and state["baseUrl"] == base_url
                             and state["clientFingerprint"] == client, "reset_environment_mismatch")
            continue_run(path, state, database, sql)
            print(tink.encode({"manifest": str(path), "completed": True, "users": [
                {key: entry[key] for key in ("fourMoneyUserId", "key", "tinkUserId")} for entry in state["users"]]}))
            return 0
    except (tink.ConnectorError, Timeout, OSError, ValueError, TypeError, KeyError, OverflowError, KeyboardInterrupt) as error:
        code = error.code if isinstance(error, tink.ConnectorError) else (
            "reset_run_locked" if isinstance(error, Timeout) else
            "interrupted" if isinstance(error, KeyboardInterrupt) else "reset_configuration_or_io_error")
        print(tink.encode({"error": code, "manifest": str(path) if path else None,
                           "resume": "resume " + str(run_id) if path and path.exists() else None}), file=sys.stderr)
        return 130 if isinstance(error, KeyboardInterrupt) else 1


def plan(mapping=str(DEFAULT_MAPPING), run_id=None, as_of_date=None):
    """Validate mapping and preview the reset offline without creating files."""
    return execute("plan", mapping=mapping, run_id=run_id, as_of_date=as_of_date)


def reset(mapping=str(DEFAULT_MAPPING), run_id=None, as_of_date=None, runs_dir=str(DEFAULT_RUNS)):
    """Replace local SQL data, create fresh Tink fixtures and bind mapped users."""
    return execute("reset", mapping=mapping, run_id=run_id, as_of_date=as_of_date, runs_dir=runs_dir)


def resume(run, runs_dir=str(DEFAULT_RUNS)):
    """Continue a saved reset without repeating a confirmed SQL reset."""
    return execute("resume", run=run, runs_dir=runs_dir)


def main(argv=None):
    result = fire.Fire({"plan": plan, "reset": reset, "resume": resume}, command=argv,
                       name="reset_state", serialize=lambda _: "")
    return result if type(result) is int else 0


if __name__ == "__main__":
    sys.exit(main())
