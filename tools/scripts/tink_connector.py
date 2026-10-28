"""Seed synthetic Tink Data v2 fixtures with durable checkpoints; never replay uncertain writes."""

import copy
import hashlib
import math
import os
import re
import socket
import sys
import tempfile
import time
import uuid
from contextlib import contextmanager
from datetime import date, datetime, timedelta, timezone
from decimal import Decimal, InvalidOperation, localcontext
from pathlib import Path
from types import SimpleNamespace
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

import fire
import simplejson as json
from filelock import FileLock, Timeout

ROOT = Path(__file__).resolve().parents[2]
PRESETS = Path(__file__).resolve().parent / "tink_connector" / "presets"
DEFAULT_RUNS = ROOT / ".cache" / "tink-connector"
KEY = re.compile(r"[A-Za-z0-9][A-Za-z0-9_-]{0,63}\Z")
BALANCE_KEYS = {"bookedBalance", "availableBalanceIncludingCredit", "creditLimit"}


class ConnectorError(Exception):
    def __init__(self, code, *, status=None, retry_after=0):
        super().__init__(code)
        self.code = code
        self.status = status
        self.retry_after = retry_after


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def encode(value):
    return json.dumps(value, use_decimal=True, allow_nan=False, sort_keys=True, ensure_ascii=False)


def digest(value):
    return hashlib.sha256(encode(value).encode("utf-8")).hexdigest()


def read_json(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"), use_decimal=True)
    except (OSError, ValueError, TypeError):
        raise ConnectorError("invalid_json_file") from None


def require(condition, code):
    if not condition:
        raise ConnectorError(code)


def text(value):
    return isinstance(value, str) and bool(value.strip())


def identifier(value):
    return text(value) and len(value) <= 255 and not any(ord(char) < 32 for char in value)


def local_user_id(value):
    try:
        parsed = uuid.UUID(str(value))
        require(parsed.int != 0, "invalid_four_money_user_id")
        return str(parsed)
    except (ValueError, TypeError, AttributeError):
        raise ConnectorError("invalid_four_money_user_id") from None


class LocalDatabase:
    """Local-only identity binding; never persists tokens or creates application users."""

    def __init__(self):
        import psycopg
        from psycopg.conninfo import conninfo_to_dict

        self.psycopg = psycopg
        self.dsn = os.environ.get("FOURMONEY_DATABASE_DSN", "")
        require(text(self.dsn), "missing_FOURMONEY_DATABASE_DSN")
        try:
            settings = conninfo_to_dict(self.dsn)
        except psycopg.Error:
            raise ConnectorError("invalid_local_database_dsn") from None
        loopback = {"localhost", "127.0.0.1", "::1"}
        require(settings.get("host") in loopback
                and (not settings.get("hostaddr") or settings["hostaddr"] in loopback)
                and not settings.get("service"), "local_database_required")

    @contextmanager
    def connection(self):
        try:
            with self.psycopg.connect(self.dsn, connect_timeout=5) as connection:
                # Bound database waits too: a concurrent provisioning operation may hold a lock.
                connection.execute("SET LOCAL lock_timeout = '5s'")
                connection.execute("SET LOCAL statement_timeout = '10s'")
                yield connection
        except self.psycopg.errors.UniqueViolation:
            raise ConnectorError("local_mapping_conflict") from None
        except self.psycopg.Error:
            raise ConnectorError("local_database_unavailable") from None

    @staticmethod
    def profile(connection, user_id, *, lock=False):
        row = connection.execute(
            'SELECT is_active, is_email_confirmed, tink_user_id FROM "user"."user" WHERE _id = %s'
            + (" FOR UPDATE" if lock else ""), (user_id,)).fetchone()
        require(row is not None, "local_user_not_found")
        require(row[0], "local_user_inactive")
        require(row[1], "local_user_unconfirmed")
        return row[2] if row[2] and row[2].strip() else None

    def preflight(self, user_id):
        with self.connection() as connection:
            return self.profile(connection, user_id)

    def bind(self, user_id, tink_user_id):
        require(identifier(tink_user_id), "invalid_binding_tink_user_id")
        lock_id = int.from_bytes(hashlib.sha256(
            ("4money/tink-user/" + uuid.UUID(user_id).hex).encode()).digest()[:8], "little", signed=True)
        with self.connection() as connection:
            connection.execute("SELECT pg_advisory_xact_lock(%s)", (lock_id,))
            current = self.profile(connection, user_id, lock=True)
            if current != tink_user_id:
                connection.execute('UPDATE "user"."user" SET tink_user_id = %s WHERE _id = %s',
                                   (tink_user_id, user_id))


def bind_local_user(store, database):
    binding = store.state["localBinding"]
    binding.update(status="binding", tinkUserId=store.state["tinkUserId"])
    store.save()  # A crash after commit must be recoverable from this durable intent.
    database.bind(binding["userId"], binding["tinkUserId"])
    binding.update(status="bound")
    store.save()
    store.log("local_user_bound", fourMoneyUserId=binding["userId"], tinkUserId=binding["tinkUserId"])


def allowed_fields(value, allowed, required):
    require(isinstance(value, dict) and set(value) <= set(allowed) and set(required) <= set(value), "invalid_preset_fields")


def validate_preset(preset):
    allowed_fields(preset, {"version", "name", "market", "locale", "accounts"}, {"version", "name", "market", "locale", "accounts"})
    require(type(preset["version"]) is int and preset["version"] == 1, "unsupported_preset_version")
    require(text(preset["name"]) and text(preset["market"]) and re.fullmatch(r"[A-Z]{2}", preset["market"])
            and text(preset["locale"]) and re.fullmatch(r"[a-z]{2}_[A-Z]{2}", preset["locale"]), "invalid_preset_metadata")
    require(isinstance(preset["accounts"], list) and bool(preset["accounts"]), "empty_preset")
    keys = set()
    for account in preset["accounts"]:
        allowed_fields(account, {"key", "name", "type", "currencyCode", "minorUnit", "displayAccountNumber", "balances", "transactions"},
                       {"key", "name", "type", "currencyCode", "minorUnit", "balances", "transactions"})
        require(text(account["key"]) and KEY.fullmatch(account["key"]) and account["key"] not in keys, "invalid_account_key")
        keys.add(account["key"])
        require(account["type"] in {"CHECKING", "SAVINGS", "CREDIT_CARD"} and text(account["name"]), "invalid_account")
        require(isinstance(account["currencyCode"], str) and re.fullmatch(r"[A-Z]{3}", account["currencyCode"]), "invalid_currency")
        require(type(account["minorUnit"]) is int and account["minorUnit"] == 2, "invalid_minor_unit")
        if account["currencyCode"] in {"PLN", "EUR"}:
            require(account["minorUnit"] == 2, "invalid_minor_unit")
        if "displayAccountNumber" in account:
            require(text(account["displayAccountNumber"]), "invalid_account_number")
        balances = account["balances"]
        require(isinstance(balances, dict) and bool(balances) and set(balances) <= BALANCE_KEYS, "invalid_balances")
        require(all(isinstance(value, str) and re.fullmatch(r"-?[0-9]{1,24}", value) for value in balances.values()), "invalid_balance_minor_units")
        require(isinstance(account["transactions"], list), "invalid_transactions")
        tx_keys = set()
        for transaction in account["transactions"]:
            allowed_fields(transaction, {"key", "amount", "description", "status", "dayOffset", "timeUtc"},
                           {"key", "amount", "description", "status", "dayOffset"})
            key = transaction["key"]
            require(text(key) and KEY.fullmatch(key) and key not in tx_keys, "invalid_transaction_key")
            tx_keys.add(key)
            amount = transaction["amount"]
            require(isinstance(amount, str) and re.fullmatch(r"-?[0-9]{1,18}(?:\.[0-9]{1,6})?", amount), "invalid_decimal_amount")
            require(max(0, -Decimal(amount).as_tuple().exponent) <= account["minorUnit"], "amount_exceeds_currency_precision")
            require(text(transaction["description"]) and transaction["status"] in {"PENDING", "BOOKED"}, "invalid_transaction")
            require(type(transaction["dayOffset"]) is int and -36500 <= transaction["dayOffset"] <= 36500, "invalid_date_offset")
            try:
                clock = transaction.get("timeUtc", "12:00:00")
                require(isinstance(clock, str) and re.fullmatch(r"[0-9]{2}:[0-9]{2}:[0-9]{2}", clock), "invalid_transaction_time")
                datetime.strptime(clock, "%H:%M:%S")
            except (TypeError, ValueError):
                raise ConnectorError("invalid_transaction_time") from None
    return preset


def materialize(preset, run_id, as_of):
    namespace = uuid.uuid5(uuid.NAMESPACE_URL, "4money/tink-fixtures/" + run_id)
    accounts = []
    for source in preset["accounts"]:
        account = {"key": source["key"], "externalAccountId": str(uuid.uuid5(namespace, "account/" + source["key"])),
                   "accountName": source["name"], "type": source["type"], "currencyCode": source["currencyCode"],
                   "minorUnit": source["minorUnit"], "balances": copy.deepcopy(source["balances"]), "transactions": []}
        if "displayAccountNumber" in source:
            account["displayAccountNumber"] = source["displayAccountNumber"]
        for transaction in source["transactions"]:
            day = as_of + timedelta(days=transaction["dayOffset"])
            timestamp = day.isoformat() + "T" + transaction.get("timeUtc", "12:00:00") + ".000Z"
            account["transactions"].append({
                "externalTransactionId": str(uuid.uuid5(namespace, "transaction/" + source["key"] + "/" + transaction["key"])),
                "reference": str(uuid.uuid5(namespace, "transaction/" + source["key"] + "/" + transaction["key"])),
                "amount": Decimal(transaction["amount"]), "descriptions": {"original": transaction["description"]},
                "status": transaction["status"],
                "bookedDateTime" if transaction["status"] == "BOOKED" else "transactionDateTime": timestamp,
            })
        accounts.append(account)
    return accounts


def atomic_write(path, value):
    path = Path(path)
    fd, temporary = tempfile.mkstemp(prefix=".manifest-", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            handle.write(encode(value) + "\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
        if os.name == "posix":
            directory = os.open(path.parent, os.O_RDONLY)
            try:
                os.fsync(directory)
            finally:
                os.close(directory)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


class RunStore:
    def __init__(self, path, state):
        self.path, self.state = Path(path), state
        self.events = self.path.parent / "events.jsonl"

    def save(self):
        self.state["updatedAt"] = utc_now()
        try:
            atomic_write(self.path, self.state)
        except OSError:
            # The stable external ID was persisted before the request, so user recovery remains possible.
            print(encode({"event": "manifest_write_failed", "manifest": str(self.path),
                          "externalUserId": self.state.get("externalUserId"), "tinkUserId": self.state.get("tinkUserId"),
                          "operationId": self.state.get("operationId")}), file=sys.stderr, flush=True)
            raise ConnectorError("manifest_write_failed") from None

    def log(self, event, **fields):
        # Call sites supply only metadata and IDs of synthetic resources, never HTTP payloads.
        entry = {"time": utc_now(), "event": event, "runId": self.state["runId"], **fields}
        print(encode(entry), file=sys.stderr, flush=True)
        try:
            with self.events.open("a", encoding="utf-8") as handle:
                handle.write(encode(entry) + "\n")
                handle.flush()
                os.fsync(handle.fileno())
        except OSError:
            # A failed auxiliary log must not discard an ID returned by a successful write.
            print(encode({"event": "event_log_write_failed", "manifest": str(self.path)}), file=sys.stderr, flush=True)

    def checkpoint(self, stage, **updates):
        self.state.update(updates)
        self.state["stage"] = stage
        self.save()
        self.log("checkpoint", stage=stage, tinkUserId=self.state.get("tinkUserId"), operationId=self.state.get("operationId"))


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class TinkClient:
    def __init__(self, base_url, client_id, secret, store, timeout=30):
        self.base_url, self.client_id, self.secret = base_url.rstrip("/"), client_id, secret
        self.store, self.timeout = store, timeout
        self.tokens = {}
        self.opener = build_opener(NoRedirect())

    def request(self, method, path, *, token=None, data=None, form=None, query=None):
        headers = {"Accept": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        body = None
        if form is not None:
            headers["Content-Type"] = "application/x-www-form-urlencoded"
            body = urlencode(form).encode()
        elif data is not None:
            headers["Content-Type"] = "application/json"
            body = encode(data).encode("utf-8")
        url = self.base_url + path + (("?" + urlencode(query)) if query else "")
        started = time.monotonic()
        status = None
        try:
            with self.opener.open(Request(url, data=body, method=method, headers=headers), timeout=self.timeout) as response:
                status = response.status
                raw = response.read()
            if not 200 <= status < 300:
                raise ConnectorError("unexpected_http_status", status=status)
            if not raw.strip():
                return {}
            try:
                result = json.loads(raw, use_decimal=True)
            except (ValueError, UnicodeError):
                raise ConnectorError("invalid_json_response", status=status) from None
            require(isinstance(result, dict), "invalid_response_object")
            return result
        except HTTPError as error:
            status = error.code
            try:
                retry = min(10, max(0, float(error.headers.get("Retry-After", "0"))))
            except (TypeError, ValueError):
                retry = 0
            error.close()
            raise ConnectorError("http_error", status=status, retry_after=retry) from None
        except (socket.timeout, TimeoutError):
            raise ConnectorError("http_timeout") from None
        except (URLError, ConnectionError, OSError):
            raise ConnectorError("network_error") from None
        finally:
            self.store.log("http", method=method, path=path, status=status, elapsedMs=round((time.monotonic() - started) * 1000))

    def client_token(self, scope, fresh=False):
        key = ("client", scope)
        cached = self.tokens.get(key)
        if not fresh and cached and cached[1] > time.monotonic():
            return cached[0]
        started = time.monotonic()
        response = self.request("POST", "/api/v1/oauth/token", form={"client_id": self.client_id, "client_secret": self.secret,
                                "grant_type": "client_credentials", "scope": scope})
        return self.remember_token(key, response, started)

    def remember_token(self, key, response, started):
        expires = response.get("expires_in")
        require(text(response.get("access_token")) and type(expires) in (int, Decimal) and 0 < expires <= 86400 * 365, "invalid_token_response")
        lifetime = float(expires) - min(60, float(expires) * 0.1)
        require(started + lifetime > time.monotonic(), "expired_token_response")
        self.tokens[key] = (response["access_token"], started + lifetime)
        return response["access_token"]

    def user_token(self, external_user_id, scope, fresh=False):
        key = (external_user_id, scope)
        cached = self.tokens.get(key)
        if not fresh and cached and cached[1] > time.monotonic():
            return cached[0]
        grant = self.request("POST", "/api/v1/oauth/authorization-grant", token=self.client_token("authorization:grant"),
                             form={"external_user_id": external_user_id, "scope": scope})
        require(text(grant.get("code")), "invalid_grant_response")
        started = time.monotonic()
        response = self.request("POST", "/api/v1/oauth/token", form={"client_id": self.client_id, "client_secret": self.secret,
                                "grant_type": "authorization_code", "code": grant["code"]})
        return self.remember_token(key, response, started)

    def read(self, path, scope, external_user_id=None, query=None):
        for attempt in range(2):
            token = (self.user_token(external_user_id, scope, fresh=bool(attempt)) if external_user_id
                     else self.client_token(scope, fresh=bool(attempt)))
            try:
                return self.request("GET", path, token=token, query=query)
            except ConnectorError as error:
                if error.status != 401 or attempt:
                    raise
        raise ConnectorError("unauthorized")

    def pages(self, path, field, external_user_id, query=None):
        query = {"pageSize": 100, **(query or {})}
        seen = set()
        items = []
        deadline = time.monotonic() + 120
        while True:
            require(time.monotonic() < deadline and len(seen) < 10000, "pagination_limit")
            response = self.read(path, "accounts:read,transactions:read", external_user_id, query)
            require(isinstance(response.get(field), list) and all(isinstance(item, dict) for item in response[field]), "invalid_page")
            items.extend(response[field])
            page = response.get("nextPageToken")
            if page in (None, ""):
                return items
            require(text(page) and page not in seen, "pagination_loop")
            seen.add(page)
            query = {**query, "pageToken": page}


def poll(action, timeout, store, label):
    deadline = time.monotonic() + timeout
    delay = 1
    while True:
        try:
            if action():
                return
        except ConnectorError as error:
            if error.status not in {429, 500, 502, 503, 504} and error.code not in {"network_error", "http_timeout"}:
                raise
            store.log("read_retry", stage=label, error=error.code, status=error.status)
            delay = max(delay, error.retry_after)
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise ConnectorError(label + "_timeout")
        time.sleep(min(delay, remaining))
        delay = min(10, delay * 2)


def parse_money(value):
    require(isinstance(value, dict) and text(value.get("currencyCode")), "invalid_read_amount")
    number = value.get("value")
    require(isinstance(number, dict), "invalid_read_amount")
    try:
        scale = int(number["scale"])
        unscaled = str(number["unscaledValue"])
        require(-18 <= scale <= 18 and re.fullmatch(r"-?[0-9]{1,40}", unscaled), "invalid_read_amount")
        with localcontext() as context:
            context.prec = 64
            return Decimal(unscaled).scaleb(-scale), value["currencyCode"]
    except (KeyError, ValueError, TypeError, InvalidOperation):
        raise ConnectorError("invalid_read_amount") from None


def response_issues(response, expected_external_user_id):
    """Inspect structured errors without retaining provider messages or whole response bodies."""
    issues = []
    users = response.get("users", [])
    require(isinstance(users, list), "invalid_operation_users")
    for user in users:
        require(isinstance(user, dict) and user.get("externalUserId") == expected_external_user_id, "unexpected_operation_user")
        exceptions = user.get("exceptions", [])
        require(isinstance(exceptions, list), "invalid_operation_exceptions")
        for error in exceptions:
            require(isinstance(error, dict), "invalid_operation_exception")
            # IDs and enum-like reason codes only. Arbitrary provider messages are never persisted.
            safe = {key: str(error[key])[:255] for key in ("fieldPath", "externalAccountId", "externalTransactionId") if key in error}
            reasons = error.get("reasons", [])
            require(isinstance(reasons, list), "invalid_exception_reasons")
            safe["reasons"] = [reason["errorType"] for reason in reasons
                               if isinstance(reason, dict) and isinstance(reason.get("errorType"), str)
                               and re.fullmatch(r"[A-Z][A-Z0-9_]{0,100}", reason["errorType"])]
            issues.append(safe)
    return issues


class Seeder:
    def __init__(self, store, client, wait_timeout=120):
        self.store, self.client, self.wait_timeout = store, client, wait_timeout
        self.state = store.state

    def identity(self):
        user = self.client.read("/api/v1/user", "user:read", self.state["externalUserId"])
        require(identifier(user.get("id")) and user.get("externalUserId") == self.state["externalUserId"], "user_identity_mismatch")
        if self.state.get("tinkUserId"):
            require(self.state["tinkUserId"] == user["id"], "user_identity_mismatch")
        return user["id"]

    def ensure_user(self):
        status = self.state["userStatus"]
        if status == "ready":
            self.identity()
            return
        if status == "creating" or self.state["existingUser"]:
            self.store.checkpoint("user_ready", tinkUserId=self.identity(), userStatus="ready")
            return
        token = self.client.client_token("user:create")
        self.store.checkpoint("creating_user", userStatus="creating")
        try:
            response = self.client.request("POST", "/api/v1/user/create", token=token, data={
                "external_user_id": self.state["externalUserId"], "market": self.state["preset"]["market"], "locale": self.state["preset"]["locale"]})
            require(identifier(response.get("user_id")) and response.get("external_user_id") == self.state["externalUserId"], "invalid_create_user_response")
            user_id = response["user_id"]
        except ConnectorError as error:
            if error.status != 409 and error.code not in {"http_timeout", "network_error", "invalid_json_response", "invalid_create_user_response"}:
                raise
            self.store.log("recover_user", externalUserId=self.state["externalUserId"])
            user_id = self.identity()
        self.store.checkpoint("user_ready", tinkUserId=user_id, userStatus="ready")

    def account_payload(self):
        return {"accounts": [{key: value for key, value in {**account, "externalUserId": self.state["externalUserId"]}.items()
                              if key not in {"key", "minorUnit", "transactions"}} for account in self.state["accounts"]]}

    def check_accounts(self):
        actual = self.client.pages("/data/v2/accounts", "accounts", self.state["externalUserId"])
        mapping = {}
        by_external = {}
        for account in actual:
            external = account.get("externalAccountId")
            if external is not None:
                require(external not in by_external, "duplicate_external_account")
                by_external[external] = account
        for expected in self.state["accounts"]:
            account = by_external.get(expected["externalAccountId"])
            if account is None:
                return False
            require(identifier(account.get("id")), "invalid_account_id")
            if account.get("name") != expected["accountName"] or account.get("type") != expected["type"]:
                return False
            mapping[expected["externalAccountId"]] = account["id"]
            self.state["accountIds"].update(mapping)
            self.store.save()
            # Verify only fields exposed by the list endpoint; creditLimit is not returned here.
            balances = account.get("balances") or {}
            require(isinstance(balances, dict), "invalid_read_balances")
            checked = []
            for source, target in (("bookedBalance", "booked"), ("availableBalanceIncludingCredit", "available")):
                if source not in expected["balances"] or balances.get(target) is None:
                    continue
                balance = balances[target]
                require(isinstance(balance, dict), "invalid_read_balance")
                value, currency = parse_money(balance.get("amount"))
                with localcontext() as context:
                    context.prec = 64
                    wanted = Decimal(expected["balances"][source]).scaleb(-expected["minorUnit"])
                if currency != expected["currencyCode"] or value != wanted:
                    return False
                checked.append(source)
            unchecked = sorted(set(expected["balances"]) - set(checked))
            self.state.setdefault("accountVerification", {})[expected["externalAccountId"]] = {
                "verifiedBalances": checked, "uncheckedBalances": unchecked}
            self.store.save()
            self.store.log("account_balances_checked", accountId=account["id"], verifiedBalances=checked, uncheckedBalances=unchecked)
            mapping[expected["externalAccountId"]] = account["id"]
        self.state["accountIds"] = mapping
        self.store.save()
        return True

    def ensure_accounts(self):
        if self.state["accountsStatus"] == "planned":
            token = self.client.client_token("accounts:write")
            self.store.checkpoint("submitting_accounts", accountsStatus="submitting")
            try:
                response = self.client.request("POST", "/data/v2/connector/accounts", token=token, data=self.account_payload())
                issues = response_issues(response, self.state["externalUserId"])
                self.store.checkpoint("accounts_submitted", accountsStatus="submitted", accountIssues=issues)
                if issues:
                    raise ConnectorError("account_partial_failure")
            except ConnectorError as error:
                if error.status != 409:
                    raise
                self.store.log("reconcile_existing_accounts")
        if self.state.get("accountIssues"):
            raise ConnectorError("account_partial_failure")
        poll(self.check_accounts, self.wait_timeout, self.store, "accounts_visibility")
        self.store.checkpoint("accounts_verified", accountsStatus="verified", accountIds=self.state["accountIds"])

    def ensure_transactions(self):
        if self.state["transactionsStatus"] == "processed":
            return
        if not any(account["transactions"] for account in self.state["accounts"]):
            self.store.checkpoint("transactions_empty", transactionsStatus="empty")
            return
        if self.state["transactionsStatus"] == "planned":
            token = self.client.client_token("transactions:write")
            self.store.checkpoint("submitting_transactions", transactionsStatus="submitting")
            body = {"externalOperationId": self.state["externalOperationId"], "operationPriorityType": "BACKGROUND", "users": [{
                "externalUserId": self.state["externalUserId"], "accounts": [
                    {"externalAccountId": account["externalAccountId"], "transactions": account["transactions"]}
                    for account in self.state["accounts"] if account["transactions"]]}]}
            response = self.client.request("POST", "/data/v2/connector/transactions", token=token, data=body)
            require(identifier(response.get("operationId")), "missing_operation_id")
            self.store.checkpoint("transactions_submitted", operationId=response["operationId"], transactionsStatus="submitted")
            require(response.get("externalOperationId", self.state["externalOperationId"]) == self.state["externalOperationId"], "operation_identity_mismatch")
        require(identifier(self.state.get("operationId")), "unknown_transaction_write_requires_review")
        poll(self.check_operation, self.wait_timeout, self.store, "operation")
        self.store.checkpoint("transactions_processed", transactionsStatus="processed")

    def check_operation(self):
        response = self.client.read("/data/v2/connector/operations/" + quote(self.state["operationId"], safe=""), "transactions:write")
        require(response.get("id") == self.state["operationId"]
                and response.get("externalOperationId", self.state["externalOperationId"]) == self.state["externalOperationId"],
                "operation_identity_mismatch")
        issues = response_issues(response, self.state["externalUserId"])
        users = response.get("users")
        require(isinstance(users, list) and bool(users), "invalid_operation_users")
        statuses = [user.get("status") for user in users]
        require(all(status in {"PROCESSING", "PROCESSED", "FAILED"} for status in statuses), "invalid_operation_status")
        self.state["operationStatuses"] = statuses
        self.state["operationIssues"] = issues
        self.store.save()
        if issues or "FAILED" in statuses:
            self.store.log("operation_failed", issues=issues)
            raise ConnectorError("operation_failed")
        return all(status == "PROCESSED" for status in statuses)

    def check_transactions(self):
        mapping = {}
        for account in self.state["accounts"]:
            actual = self.client.pages("/data/v2/transactions", "transactions", self.state["externalUserId"],
                                       {"accountIdIn": self.state["accountIds"][account["externalAccountId"]]})
            by_external = {}
            for transaction in actual:
                external = transaction.get("reference")
                if external is not None:
                    require(external not in by_external, "duplicate_external_transaction")
                    by_external[external] = transaction
            for expected in account["transactions"]:
                transaction = by_external.get(expected["externalTransactionId"])
                if transaction is None:
                    return False
                require(identifier(transaction.get("id")), "invalid_transaction_id")
                require(transaction.get("accountId") == self.state["accountIds"][account["externalAccountId"]], "transaction_account_mismatch")
                amount, currency = parse_money(transaction.get("amount"))
                if amount != expected["amount"] or currency != account["currencyCode"]:
                    return False
                descriptions = transaction.get("descriptions")
                require(isinstance(descriptions, dict), "invalid_read_descriptions")
                if transaction.get("status") != expected["status"] or descriptions.get("original") != expected["descriptions"]["original"]:
                    return False
                for source, target in (("bookedDateTime", "booked"), ("transactionDateTime", "transaction")):
                    if source in expected:
                        observed = transaction.get(source)
                        dates = transaction.get("dates", {})
                        require(isinstance(dates, dict), "invalid_read_dates")
                        try:
                            if observed is not None:
                                if not text(observed) or datetime.fromisoformat(observed.replace("Z", "+00:00")) != datetime.fromisoformat(expected[source].replace("Z", "+00:00")):
                                    return False
                            elif dates.get(target) != expected[source][:10]:
                                return False
                        except ValueError:
                            raise ConnectorError("invalid_read_date") from None
                mapping[expected["externalTransactionId"]] = transaction["id"]
                self.state["transactionIds"].update(mapping)
                self.store.save()
        self.state["transactionIds"] = mapping
        self.store.save()
        return True

    def verify(self):
        self.state["lastVerification"] = {"status": "checking", "time": utc_now()}
        self.store.save()
        self.state["tinkUserId"] = self.identity()
        self.store.save()
        poll(self.check_accounts, self.wait_timeout, self.store, "accounts_verification")
        poll(self.check_transactions, self.wait_timeout, self.store, "transactions_verification")
        self.state["lastVerification"] = {"status": "verified", "time": utc_now()}
        self.store.save()
        self.store.log("data_verified", accounts=len(self.state["accountIds"]), transactions=len(self.state["transactionIds"]))

    def seed(self):
        self.ensure_user()
        self.ensure_accounts()
        self.ensure_transactions()
        self.verify()
        self.store.checkpoint("completed")


def create_state(preset, run_id, as_of, base_url, client_id, existing=None, four_money_user_id=None):
    require(KEY.fullmatch(run_id) is not None, "invalid_run_id")
    require(existing is None or (identifier(existing) and existing.startswith("4money-test-")), "existing_user_requires_test_prefix")
    accounts = materialize(preset, run_id, as_of)
    require(not (existing is not None and four_money_user_id is not None), "choose_existing_or_four_money_user")
    user_id = local_user_id(four_money_user_id) if four_money_user_id is not None else None
    external = "4money-" + uuid.UUID(user_id).hex if user_id else existing or "4money-test-" + run_id
    state = {"version": 1, "apiVersion": "data-v2", "runId": run_id, "createdAt": utc_now(), "stage": "planned",
            "baseUrl": base_url, "clientFingerprint": hashlib.sha256(client_id.encode()).hexdigest(),
            "preset": preset, "presetHash": digest(preset), "asOfDate": as_of.isoformat(),
            "externalUserId": external, "existingUser": existing is not None, "tinkUserId": None,
            "accounts": accounts, "accountIds": {}, "transactionIds": {}, "userStatus": "planned", "accountsStatus": "planned",
            "transactionsStatus": "planned", "externalOperationId": str(uuid.uuid4()), "operationId": None}
    if user_id:
        state["localBinding"] = {"userId": user_id, "status": "pending", "tinkUserId": None}
    return state


def validate_manifest(state, client_id, base_url):
    require(isinstance(state, dict) and state.get("version") == 1 and state.get("apiVersion") == "data-v2", "invalid_manifest")
    require(state.get("baseUrl") == base_url and state.get("clientFingerprint") == hashlib.sha256(client_id.encode()).hexdigest(), "manifest_environment_mismatch")
    preset = validate_preset(state.get("preset"))
    require(state.get("presetHash") == digest(preset), "manifest_preset_mismatch")
    require(KEY.fullmatch(state.get("runId", "")) is not None, "invalid_run_id")
    frozen = materialize(preset, state["runId"], date.fromisoformat(state["asOfDate"]))
    require(frozen == state.get("accounts"), "manifest_fixtures_mismatch")
    binding = state.get("localBinding")
    canonical = None
    if binding is not None:
        require(isinstance(binding, dict) and local_user_id(binding.get("userId")) == binding["userId"]
                and binding.get("status") in {"pending", "binding", "bound", "failed"}
                and (binding.get("tinkUserId") is None or (identifier(binding["tinkUserId"])
                     and binding["tinkUserId"] == state.get("tinkUserId"))), "invalid_manifest_binding")
        canonical = "4money-" + uuid.UUID(binding["userId"]).hex
    external = state.get("externalUserId")
    require(identifier(external) and (external.startswith("4money-test-") or external == canonical), "invalid_manifest_user")
    require(type(state.get("existingUser")) is bool and (state["existingUser"]
            or external == "4money-test-" + state["runId"] or external == canonical), "invalid_manifest_user")
    require(str(uuid.UUID(state["externalOperationId"])) == state["externalOperationId"], "invalid_manifest_operation")
    require(isinstance(state.get("accountIds"), dict) and isinstance(state.get("transactionIds"), dict), "invalid_manifest_ids")
    require(state.get("userStatus") in {"planned", "creating", "ready"}
            and state.get("accountsStatus") in {"planned", "submitting", "submitted", "verified"}
            and state.get("transactionsStatus") in {"planned", "submitting", "submitted", "processed", "empty"}, "invalid_manifest_checkpoint")


def print_human_summary(state, manifest):
    """Show confirmed resource IDs; operation status is only the last saved observation."""
    print("\nRun: " + state["runId"])
    print("Verification: verified")
    print("Manifest: " + str(manifest))
    origin = "existing" if state["existingUser"] else "created"
    print("user - " + origin + ", verified - " + state["tinkUserId"])
    print("  externalUserId: " + state["externalUserId"])
    for account in state["accounts"]:
        account_id = state["accountIds"][account["externalAccountId"]]
        print("account [" + account["key"] + "] - created, verified - " + account_id)
        print("  externalAccountId: " + account["externalAccountId"])
        checks = state.get("accountVerification", {}).get(account["externalAccountId"], {})
        print("  balances verified: " + (", ".join(checks.get("verifiedBalances", [])) or "none"))
        print("  balances unchecked: " + (", ".join(checks.get("uncheckedBalances", [])) or "none"))
        for transaction in account["transactions"]:
            external = transaction["externalTransactionId"]
            print("  transaction - created, verified - " + state["transactionIds"][external] + " - " + transaction["status"])
            print("    externalTransactionId: " + external)
    if state.get("operationId"):
        print("operation - " + state["transactionsStatus"] + " (manifest) - " + state["operationId"])
    elif state["transactionsStatus"] == "empty":
        print("operation - not submitted (no transactions)")
    else:
        print("operation - unknown (no confirmed operationId)")
    print("  externalOperationId: " + state["externalOperationId"])


def execute(command, *, preset="basic-pln", run_id=None, as_of_date=None, existing_external_user_id=None,
            runs_dir=DEFAULT_RUNS, manifest=None, run=None, base_url=None, http_timeout=30, wait_timeout=120, hr=False,
            four_money_user_id=None):
    store = None
    try:
        require(type(hr) is bool, "invalid_hr_flag")
        require(run is None or (isinstance(run, str) and KEY.fullmatch(run)), "invalid_run_id")
        require(not (run is not None and manifest is not None), "choose_run_or_manifest")
        require(command in {"plan", "seed"} or run is not None or manifest is not None, "run_or_manifest_required")
        args = SimpleNamespace(command=command, preset=preset, run_id=run_id,
                               as_of_date=date.fromisoformat(str(as_of_date)) if as_of_date is not None else None,
                               existing_external_user_id=existing_external_user_id, runs_dir=Path(runs_dir),
                               manifest=Path(manifest) if manifest is not None else DEFAULT_RUNS / str(run) / "manifest.json",
                               base_url=base_url if base_url is not None else os.environ.get("Tink__ApiBaseUrl", "https://api.tink.com"),
                               http_timeout=float(http_timeout), wait_timeout=float(wait_timeout))
        parsed_url = urlsplit(args.base_url)
        require(not parsed_url.username and not parsed_url.password and not parsed_url.query and not parsed_url.fragment,
                "invalid_base_url")
        require(parsed_url.scheme == "https" or (parsed_url.scheme == "http" and parsed_url.hostname in {"127.0.0.1", "localhost", "::1"}), "https_required")
        require(bool(parsed_url.netloc) and math.isfinite(args.http_timeout) and math.isfinite(args.wait_timeout) and args.http_timeout > 0 and args.wait_timeout > 0, "invalid_timeouts_or_url")
        base_url = args.base_url.rstrip("/")
        client_id, secret = os.environ.get("Tink__ClientId", ""), os.environ.get("Tink__ClientSecret", "")
        if args.command in {"plan", "seed"}:
            path = PRESETS / "basic-pln.json" if args.preset == "basic-pln" else Path(args.preset)
            preset = validate_preset(read_json(path))
            run_id = args.run_id or datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + uuid.uuid4().hex[:8]
            state = create_state(preset, run_id, args.as_of_date or datetime.now(timezone.utc).date(), base_url,
                                 client_id, args.existing_external_user_id, four_money_user_id)
            if args.command == "plan":
                print(encode({"runId": run_id, "externalUserId": state["externalUserId"], "asOfDate": state["asOfDate"],
                              "accounts": len(state["accounts"]), "transactions": sum(len(item["transactions"]) for item in state["accounts"])}))
                return 0
            require(text(client_id) and text(secret), "missing_Tink__ClientId_or_Tink__ClientSecret")
            directory = args.runs_dir.resolve() / run_id
            directory.mkdir(parents=True, exist_ok=True)
            manifest = directory / "manifest.json"
        else:
            require(text(client_id) and text(secret), "missing_Tink__ClientId_or_Tink__ClientSecret")
            manifest = args.manifest.resolve()
            require(manifest.is_file(), "manifest_not_found")
        with FileLock(str(manifest.parent / "run.lock"), timeout=0):
            if args.command == "seed":
                require(not manifest.exists(), "run_exists_use_resume")
                store = RunStore(manifest, state)
                store.save()  # Durable identity and write intent must precede remote mutations.
            else:
                state = read_json(manifest)
                validate_manifest(state, client_id, base_url)
                store = RunStore(manifest, state)
            store.log("run_opened", command=args.command, manifest=str(manifest), externalUserId=state["externalUserId"])
            seeder = Seeder(store, TinkClient(base_url, client_id, secret, store, args.http_timeout), args.wait_timeout)
            try:
                if command == "link-db":
                    require(state.get("stage") == "completed", "run_not_completed")
                    user_id = local_user_id(four_money_user_id)
                    binding = state.get("localBinding")
                    require(binding is None or binding["userId"] == user_id, "local_binding_user_conflict")
                    if binding is None:
                        state["localBinding"] = {"userId": user_id, "status": "pending", "tinkUserId": state["tinkUserId"]}
                        store.save()
                database = None
                if command in {"seed", "resume", "link-db"} and state.get("localBinding"):
                    database = LocalDatabase()
                    database.preflight(state["localBinding"]["userId"])
                if args.command == "verify":
                    seeder.verify()
                elif command == "link-db":
                    seeder.identity()
                else:
                    seeder.seed()
                if database is not None:
                    bind_local_user(store, database)
            except (ConnectorError, KeyboardInterrupt) as error:
                code = error.code if isinstance(error, ConnectorError) else "interrupted"
                if state.get("lastVerification", {}).get("status") == "checking":
                    state["lastVerification"] = {"status": "failed", "time": utc_now()}
                state["lastError"] = {"code": code, "status": getattr(error, "status", None), "time": utc_now()}
                if state.get("localBinding") and (code.startswith("local_") or "DATABASE_DSN" in code):
                    state["localBinding"]["status"] = "failed"
                store.save()
                store.log("run_failed", error=code, status=getattr(error, "status", None), manifest=str(manifest))
                return 130 if isinstance(error, KeyboardInterrupt) else 1
            state.pop("lastError", None)
            store.save()
            print(encode({"manifest": str(manifest), "stage": state["stage"], "externalUserId": state["externalUserId"],
                          "tinkUserId": state["tinkUserId"], "operationId": state["operationId"]}))
            if args.command == "verify" and hr:
                print_human_summary(state, manifest)
            return 0
    except Timeout:
        print("run_locked", file=sys.stderr)
        return 1
    except (ConnectorError, OSError, ValueError, TypeError, KeyError) as error:
        # No traceback / exception repr: urllib and JSON errors can contain secrets or response data.
        print(error.code if isinstance(error, ConnectorError) else "local_configuration_or_io_error", file=sys.stderr)
        return 1


def plan(preset="basic-pln", run_id=None, as_of_date=None, existing_external_user_id=None,
         runs_dir=str(DEFAULT_RUNS), base_url=None, http_timeout=30, wait_timeout=120, four_money_user_id=None):
    """Preview synthetic fixtures offline; create no files or remote resources."""
    return execute("plan", preset=preset, run_id=run_id, as_of_date=as_of_date,
                   existing_external_user_id=existing_external_user_id, runs_dir=runs_dir,
                   base_url=base_url, http_timeout=http_timeout, wait_timeout=wait_timeout, four_money_user_id=four_money_user_id)


def seed(preset="basic-pln", run_id=None, as_of_date=None, existing_external_user_id=None,
         runs_dir=str(DEFAULT_RUNS), base_url=None, http_timeout=30, wait_timeout=120, four_money_user_id=None):
    """Create and verify synthetic Tink resources, saving IDs in a durable manifest."""
    return execute("seed", preset=preset, run_id=run_id, as_of_date=as_of_date,
                   existing_external_user_id=existing_external_user_id, runs_dir=runs_dir,
                   base_url=base_url, http_timeout=http_timeout, wait_timeout=wait_timeout, four_money_user_id=four_money_user_id)


def resume(run=None, manifest=None, base_url=None, http_timeout=30, wait_timeout=120):
    """Resume a run by ID, or use --manifest for a custom manifest location."""
    return execute("resume", run=run, manifest=manifest, base_url=base_url,
                   http_timeout=http_timeout, wait_timeout=wait_timeout)


def verify(run=None, manifest=None, base_url=None, http_timeout=30, wait_timeout=120, hr=False):
    """Verify a run without remote writes; --hr adds a readable resource/ID summary."""
    return execute("verify", run=run, manifest=manifest, base_url=base_url,
                   http_timeout=http_timeout, wait_timeout=wait_timeout, hr=hr)


def link_db(run=None, manifest=None, four_money_user_id=None, base_url=None, http_timeout=30, wait_timeout=120):
    """Bind a completed, remotely verified run to an existing local 4Money user."""
    return execute("link-db", run=run, manifest=manifest, four_money_user_id=four_money_user_id,
                   base_url=base_url, http_timeout=http_timeout, wait_timeout=wait_timeout)


def main(argv=None):
    # Expose commands explicitly; Fire must not expose clients, credentials or internal helpers.
    result = fire.Fire({"plan": plan, "seed": seed, "resume": resume, "verify": verify, "link-db": link_db},
                       command=argv, name="tink", serialize=lambda _: "")
    return result if type(result) is int else 0


if __name__ == "__main__":
    sys.exit(main())
