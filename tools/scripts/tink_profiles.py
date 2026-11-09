"""Create fresh synthetic Tink profiles and bind existing local users without resetting SQL."""

import io
import os
import shlex
import sys
import uuid
from contextlib import redirect_stdout
from datetime import date, datetime, timezone
from pathlib import Path

import fire
from filelock import FileLock, Timeout

import tink_connector as tink
from tink_batch import environment, load_mapping, locked_database, validate_configuration

REPO = Path(__file__).resolve().parents[2]
DEFAULT_MAPPING = Path(__file__).resolve().parent / "tink_profiles" / "users.json"
DEFAULT_RUNS = REPO / ".cache" / "tink-profiles"


def save(path, state):
    state["updatedAt"] = tink.utc_now()
    tink.atomic_write(path, state)


def validate_state(state, run_id):
    tink.require(isinstance(state, dict) and type(state.get("version")) is int and state["version"] == 1
                 and state.get("kind") == "tink-profiles" and state.get("runId") == run_id,
                 "invalid_profiles_manifest")
    tink.require(type(state.get("completed")) is bool and isinstance(state.get("users"), list)
                 and bool(state["users"]), "invalid_profiles_manifest")
    ids, keys, runs = set(), set(), set()
    for entry in state["users"]:
        tink.require(isinstance(entry, dict), "invalid_profiles_manifest")
        user_id = tink.local_user_id(entry["fourMoneyUserId"])
        for field in ("key", "connectorRunId"):
            tink.require(isinstance(entry[field], str) and tink.KEY.fullmatch(entry[field]), "invalid_profiles_manifest")
        tink.require(user_id not in ids and entry["key"] not in keys and entry["connectorRunId"] not in runs,
                     "invalid_profiles_manifest")
        ids.add(user_id)
        keys.add(entry["key"])
        runs.add(entry["connectorRunId"])
        tink.require(all(type(entry.get(field)) is bool for field in ("started", "seedCompleted", "bound"))
                     and (not entry["seedCompleted"] or (entry["started"] and tink.identifier(entry.get("tinkUserId"))))
                     and (not entry["bound"] or entry["seedCompleted"]), "invalid_profiles_manifest")
    tink.require(not state["completed"] or all(entry["bound"] for entry in state["users"]), "invalid_profiles_manifest")
    validate_configuration(state["users"], run_id, date.fromisoformat(state["asOfDate"]), state["baseUrl"])


def check_child(manifest, entry, state):
    child = tink.read_json(manifest)
    tink.require(isinstance(child, dict), "profiles_connector_manifest_mismatch")
    binding = child.get("localBinding")
    tink.require(child["runId"] == entry["connectorRunId"]
                 and child["externalUserId"] == "4money-test-" + entry["connectorRunId"]
                 and child["asOfDate"] == state["asOfDate"]
                 and child["presetHash"] == tink.digest(entry["preset"])
                 and (binding is None or binding["userId"] == entry["fourMoneyUserId"]),
                 "profiles_connector_manifest_mismatch")
    tink.validate_manifest(child, os.environ["Tink__ClientId"], state["baseUrl"])
    if entry["seedCompleted"]:
        tink.require(child["stage"] == "completed" and child["tinkUserId"] == entry["tinkUserId"],
                     "profiles_connector_manifest_mismatch")
    return child


def connector_call(function, **kwargs):
    # The wrapper emits one final JSON result; Connector still logs progress safely to stderr.
    with redirect_stdout(io.StringIO()):
        return function(**kwargs)


def continue_run(path, state, database):
    with locked_database(database, prefix="profiles") as connection:
        # Preflight all rows and detect changed completed mappings before any remote write.
        for entry in state["users"]:
            current = tink.LocalDatabase.profile(connection, entry["fourMoneyUserId"])
            if entry["bound"]:
                tink.require(current == entry["tinkUserId"], "profiles_binding_changed")
        for entry in state["users"]:
            if entry["bound"]:
                continue
            manifest = path.parent / "connector" / entry["connectorRunId"] / "manifest.json"
            print(tink.encode({"profile": entry["key"], "status": "processing"}), file=sys.stderr)
            entry["started"] = True
            save(path, state)
            if not entry["seedCompleted"]:
                if manifest.exists():
                    check_child(manifest, entry, state)
                    result = connector_call(tink.resume, manifest=str(manifest), base_url=state["baseUrl"])
                else:
                    preset_path = path.parent / (entry["key"] + ".preset.json")
                    tink.atomic_write(preset_path, entry["preset"])
                    result = connector_call(tink.seed, preset=str(preset_path), run_id=entry["connectorRunId"],
                                            as_of_date=state["asOfDate"], runs_dir=str(path.parent / "connector"),
                                            base_url=state["baseUrl"])
                tink.require(result == 0, "profiles_connector_failed")
                child = check_child(manifest, entry, state)
                tink.require(child["stage"] == "completed", "profiles_connector_not_completed")
                entry["seedCompleted"] = True
                entry["tinkUserId"] = child["tinkUserId"]
                save(path, state)
            check_child(manifest, entry, state)
            result = connector_call(tink.link_db, manifest=str(manifest), four_money_user_id=entry["fourMoneyUserId"],
                                    base_url=state["baseUrl"])
            tink.require(result == 0, "profiles_binding_failed")
            entry["bound"] = True
            save(path, state)
            print(tink.encode({"profile": entry["key"], "status": "bound"}), file=sys.stderr)
        state["completed"] = True
        save(path, state)


def execute(command, *, mapping=DEFAULT_MAPPING, run_id=None, as_of_date=None, run=None, runs_dir=DEFAULT_RUNS):
    path = None
    try:
        if command in {"plan", "apply"}:
            users = load_mapping(mapping)
            run_id = run_id or datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + uuid.uuid4().hex[:8]
            tink.require(isinstance(run_id, str) and tink.KEY.fullmatch(run_id), "invalid_run_id")
            as_of = date.fromisoformat(str(as_of_date)) if as_of_date is not None else datetime.now(timezone.utc).date()
            validate_configuration(users, run_id, as_of)
            if command == "plan":
                print(tink.encode({"runId": run_id, "asOfDate": as_of.isoformat(), "users": [
                    {"fourMoneyUserId": entry["fourMoneyUserId"], "key": entry["key"],
                     "preset": entry["preset"]["name"]} for entry in users]}))
                return 0
        else:
            tink.require(isinstance(run, str) and tink.KEY.fullmatch(run), "invalid_run_id")
            run_id = run
        database, target, base_url, client = environment()
        directory = Path(runs_dir).resolve() / run_id
        if command == "apply":
            directory.mkdir(parents=True, exist_ok=True)
        tink.require(directory.is_dir(), "profiles_manifest_not_found")
        with FileLock(str(directory / "run.lock"), timeout=0):
            path = directory / "manifest.json"
            if command == "apply":
                tink.require(not path.exists(), "profiles_run_exists_use_resume")
                validate_configuration(users, run_id, as_of, base_url)
                for entry in users:
                    entry.update(connectorRunId="profiles-" + uuid.uuid4().hex, started=False,
                                 seedCompleted=False, bound=False, tinkUserId=None)
                state = {"version": 1, "kind": "tink-profiles", "runId": run_id, "asOfDate": as_of.isoformat(),
                         "users": users, "databaseFingerprint": target, "baseUrl": base_url,
                         "clientFingerprint": client, "completed": False}
                save(path, state)
            else:
                state = tink.read_json(path)
                validate_state(state, run_id)
                tink.require(state["databaseFingerprint"] == target and state["baseUrl"] == base_url
                             and state["clientFingerprint"] == client, "profiles_environment_mismatch")
            continue_run(path, state, database)
            print(tink.encode({"manifest": str(path), "completed": True, "users": [
                {key: entry[key] for key in ("fourMoneyUserId", "key", "tinkUserId")} for entry in state["users"]]}))
            return 0
    except (tink.ConnectorError, Timeout, OSError, ValueError, TypeError, KeyError, OverflowError, KeyboardInterrupt) as error:
        code = error.code if isinstance(error, tink.ConnectorError) else (
            "profiles_run_locked" if isinstance(error, Timeout) else
            "interrupted" if isinstance(error, KeyboardInterrupt) else "profiles_configuration_or_io_error")
        print(tink.encode({"error": code, "manifest": str(path) if path else None,
                           "resume": shlex.join(["python", "tools/scripts/tink_profiles.py", "resume", str(run_id),
                                       "--runs-dir", str(Path(runs_dir).resolve())])
                           if path and path.exists() else None}), file=sys.stderr)
        return 130 if isinstance(error, KeyboardInterrupt) else 1


def plan(mapping=str(DEFAULT_MAPPING), run_id=None, as_of_date=None):
    """Preview profiles offline; no database, HTTP or file writes."""
    return execute("plan", mapping=mapping, run_id=run_id, as_of_date=as_of_date)


def apply(mapping=str(DEFAULT_MAPPING), run_id=None, as_of_date=None, runs_dir=str(DEFAULT_RUNS)):
    """Create fresh synthetic profiles and replace mappings on existing local users."""
    return execute("apply", mapping=mapping, run_id=run_id, as_of_date=as_of_date, runs_dir=runs_dir)


def resume(run, runs_dir=str(DEFAULT_RUNS)):
    """Continue a frozen batch, keeping completed user mappings."""
    return execute("resume", run=run, runs_dir=runs_dir)


def main(argv=None):
    result = fire.Fire({"plan": plan, "apply": apply, "resume": resume}, command=argv,
                       name="tink_profiles", serialize=lambda _: "")
    return result if type(result) is int else 0


if __name__ == "__main__":
    sys.exit(main())
