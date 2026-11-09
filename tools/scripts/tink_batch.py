"""Shared configuration and local database locking for Tink batch tools."""

import hashlib
import os
from contextlib import contextmanager
from pathlib import Path

import tink_connector as tink

# Keep the existing reset lock identity so both wrappers exclude one another.
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
def locked_database(database, prefix="reset"):
    try:
        with database.psycopg.connect(database.dsn, autocommit=True, connect_timeout=5) as connection:
            connection.execute("SET lock_timeout = '5s'")
            connection.execute("SET statement_timeout = '30s'")
            tink.require(connection.execute("SELECT pg_try_advisory_lock(%s)", (DATABASE_LOCK,)).fetchone()[0],
                         prefix + "_database_locked")
            yield connection
            # Closing this dedicated session also releases the advisory lock on failures.
    except database.psycopg.Error:
        raise tink.ConnectorError(prefix + "_database_error") from None


def validate_configuration(users, run_id, as_of, base_url=None):
    if base_url is not None:
        parsed = tink.urlsplit(base_url)
        tink.require(bool(parsed.netloc) and not parsed.username and not parsed.password
                     and not parsed.query and not parsed.fragment, "invalid_base_url")
        tink.require(parsed.scheme == "https" or (parsed.scheme == "http"
                     and parsed.hostname in {"127.0.0.1", "localhost", "::1"}), "https_required")
    for entry in users:
        tink.materialize(tink.validate_preset(entry["preset"]), run_id, as_of)
