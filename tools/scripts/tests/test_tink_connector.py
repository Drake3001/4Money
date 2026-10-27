"""Run the actual CLI against a local, stateful Data v2 HTTP stand-in."""

import copy
import io
import importlib.util
import os
import socket
import subprocess
import sys
import tempfile
import threading
import unittest
import uuid
from contextlib import redirect_stderr, redirect_stdout
from datetime import date
from decimal import Decimal
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit

import fire
import simplejson as json
from filelock import FileLock

SCRIPT = Path(__file__).resolve().parents[1] / "tink_connector.py"
SPEC = importlib.util.spec_from_file_location("connector_cli", SCRIPT)
cli = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(cli)
CLIENT_ID = "synthetic-client"
SECRET = "synthetic-client-secret-never-log"
TOKEN = "synthetic-access-token-never-log"
CODE = "synthetic-authorization-code-never-log"


def money(value):
    amount = Decimal(value)
    return {"currencyCode": "PLN", "value": {"scale": str(-amount.as_tuple().exponent),
                                            "unscaledValue": str(int(amount.scaleb(-amount.as_tuple().exponent)))}}


class Sandbox:
    def __init__(self, directory):
        self.directory = directory
        self.user = None
        self.unique_user_ids = False
        self.accounts = []
        self.transactions = []
        self.requests = []
        self.mode = None
        self.processing = False
        self.operation = None
        self.manifest = directory / "case" / "manifest.json"
        self.checkpoint_seen = False
        self.granted_scope = ""
        self.token_scopes = {}
        sandbox = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def do_GET(self):
                self.handle_api()

            def do_POST(self):
                self.handle_api()

            def send(self, value, status=200):
                body = cli.encode(value).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def disconnect(self):
                self.connection.shutdown(socket.SHUT_RDWR)
                self.connection.close()

            def handle_api(self):
                parsed = urlsplit(self.path)
                path, query = parsed.path, parse_qs(parsed.query)
                raw = self.rfile.read(int(self.headers.get("Content-Length", 0)))
                form = parse_qs(raw.decode()) if "x-www-form-urlencoded" in self.headers.get("Content-Type", "") else None
                body = json.loads(raw, use_decimal=True) if raw and form is None else None
                sandbox.requests.append((self.command, path, body, form, query))
                if path == "/api/v1/oauth/token":
                    if form.get("client_secret") != [SECRET] or form.get("client_id") != [CLIENT_ID]:
                        return self.send({}, 401)
                    if form["grant_type"] == ["authorization_code"] and form.get("code") != [CODE]:
                        return self.send({}, 400)
                    scope = sandbox.granted_scope if form["grant_type"] == ["authorization_code"] else form.get("scope", [""])[0]
                    token = TOKEN + "-balances" if "accounts.balances:readonly" in scope.split(",") else TOKEN
                    sandbox.token_scopes[token] = set(scope.split(","))
                    return self.send({"access_token": token, "expires_in": 1800})
                token = self.headers.get("Authorization", "").removeprefix("Bearer ")
                if token not in sandbox.token_scopes:
                    return self.send({}, 401)
                if path == "/api/v1/user/create":
                    sandbox.user = {"id": "tink-" + uuid.uuid4().hex if sandbox.unique_user_ids else "tink-user-1",
                                    "externalUserId": body["external_user_id"]}
                    if sandbox.mode == "lost_user":
                        sandbox.mode = None
                        return self.disconnect()
                    return self.send({"user_id": sandbox.user["id"], "external_user_id": sandbox.user["externalUserId"]})
                if path == "/api/v1/oauth/authorization-grant":
                    if not sandbox.user or form.get("external_user_id") != [sandbox.user["externalUserId"]]:
                        return self.send({}, 404)
                    sandbox.granted_scope = form["scope"][0]
                    return self.send({"code": CODE})
                if path == "/api/v1/user":
                    return self.send(sandbox.user or {}, 200 if sandbox.user else 404)
                if path == "/data/v2/connector/accounts":
                    manifest = cli.read_json(sandbox.manifest)
                    sandbox.checkpoint_seen = manifest["tinkUserId"] == sandbox.user["id"] and manifest["accountsStatus"] == "submitting"
                    if sandbox.mode in {"400", "401", "403"}:
                        return self.send({"message": SECRET + TOKEN + CODE}, int(sandbox.mode))
                    sandbox.accounts = [dict(account, id="account-" + str(index)) for index, account in enumerate(body["accounts"])]
                    if sandbox.mode == "slow_accounts":
                        import time
                        time.sleep(0.15)
                        return self.disconnect()
                    if sandbox.mode == "lost_accounts":
                        sandbox.mode = None
                        return self.disconnect()
                    if sandbox.mode == "account_exception":
                        return self.send({"users": [{"externalUserId": sandbox.user["externalUserId"], "exceptions": [{
                            "fieldPath": "Account[0]", "reasons": [{"errorType": "INVALID_BALANCE", "message": SECRET}]}]}]})
                    return self.send({"users": []}, 202)
                if path == "/data/v2/accounts":
                    if sandbox.mode == "read_401":
                        sandbox.mode = None
                        return self.send({"message": TOKEN}, 401)
                    if sandbox.mode == "read_429":
                        sandbox.mode = None
                        return self.send({}, 429)
                    values = []
                    for account in sandbox.accounts:
                        balances = {target: {"amount": money(Decimal(account["balances"][source]).scaleb(-2))}
                                    for source, target in (("bookedBalance", "booked"), ("availableBalanceIncludingCredit", "available"))
                                    if source in account["balances"]}
                        if sandbox.mode == "missing_balances":
                            balances = {}
                        if sandbox.mode == "wrong_booked":
                            balances["booked"]["amount"] = money("0")
                        if sandbox.mode == "wrong_available":
                            balances["available"]["amount"] = money("0")
                        if sandbox.mode == "wrong_balance_currency":
                            balances["booked"]["amount"]["currencyCode"] = "EUR"
                        values.append({"id": account["id"], "externalAccountId": account["externalAccountId"],
                                       "name": account["accountName"], "type": account["type"], "balances": balances})
                    return self.page("accounts", values, query)
                if path.startswith("/data/v2/accounts/") and path.endswith("/balances"):
                    return self.send({"message": "Detailed balances unavailable for this application"}, 403)
                if path == "/data/v2/connector/transactions":
                    sandbox.operation = body["externalOperationId"]
                    for user in body["users"]:
                        for account in user["accounts"]:
                            internal = next(item["id"] for item in sandbox.accounts if item["externalAccountId"] == account["externalAccountId"])
                            for tx in account["transactions"]:
                                # The read API has reference and dateTime fields, not externalTransactionId.
                                result = {key: value for key, value in tx.items() if key != "externalTransactionId"}
                                result.update(id="tx-" + str(len(sandbox.transactions)), accountId=internal, amount=money(tx["amount"]))
                                sandbox.transactions.append(result)
                    if sandbox.mode == "lost_transactions":
                        sandbox.mode = None
                        return self.disconnect()
                    if sandbox.mode == "missing_operation":
                        return self.send({}, 202)
                    return self.send({"externalOperationId": sandbox.operation, "operationId": "operation-1"}, 202)
                if path == "/data/v2/connector/operations/operation-1":
                    status = "PROCESSING" if sandbox.processing or sandbox.mode == "processing" else "PROCESSED"
                    sandbox.processing = False
                    users = [{"externalUserId": sandbox.user["externalUserId"], "status": status, "exceptions": []}]
                    if sandbox.mode == "operation_exception":
                        users.append({"externalUserId": sandbox.user["externalUserId"], "status": "PROCESSED", "exceptions": [{
                            "externalTransactionId": "test-tx", "fieldPath": "User[1].Account[0].Transaction[0]",
                            "reasons": [{"errorType": "MISSING_TRANSACTION_AMOUNT", "message": SECRET + TOKEN}]}]})
                    if sandbox.mode == "failed":
                        users[-1]["status"] = "FAILED"
                    return self.send({"id": "operation-1", "externalOperationId": sandbox.operation, "users": users})
                if path == "/data/v2/transactions":
                    values = [copy.deepcopy(tx) for tx in sandbox.transactions if tx["accountId"] == query["accountIdIn"][0]]
                    if sandbox.mode == "wrong_amount":
                        for tx in values:
                            tx["amount"] = money("0")
                    if sandbox.mode == "date_only":
                        for tx in values:
                            tx["dates"] = {}
                            for source, target in (("bookedDateTime", "booked"), ("transactionDateTime", "transaction")):
                                if source in tx:
                                    tx["dates"][target] = tx.pop(source)[:10]
                    return self.page("transactions", values, query)
                return self.send({}, 404)

            def page(self, field, values, query):
                offset = int(query.get("pageToken", [0])[0])
                end = offset + 2
                self.send({field: values[offset:end], "nextPageToken": str(end) if end < len(values) else ""})

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base_url = "http://127.0.0.1:" + str(self.server.server_port)

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()


class ConnectorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)
        self.api = Sandbox(self.directory)
        self.addCleanup(self.temp.cleanup)
        self.addCleanup(self.api.close)

    def call(self, command, *extra, ok=True, credentials=True):
        arguments = [sys.executable, str(SCRIPT), command, "--base-url", self.api.base_url, "--wait-timeout", "0.15", "--http-timeout", "2"]
        if command in {"seed", "plan"}:
            arguments += ["--run-id", "case", "--as-of-date", "2026-10-23", "--runs-dir", str(self.directory)]
        else:
            arguments += ["--manifest", str(self.api.manifest)]
        env = dict(os.environ, Tink__ClientId=CLIENT_ID if credentials else "", Tink__ClientSecret=SECRET if credentials else "")
        result = subprocess.run(arguments + list(extra), env=env, capture_output=True, text=True, timeout=15)
        self.assertEqual(result.returncode, 0 if ok else 1, result.stderr)
        for secret in (SECRET, TOKEN, CODE):
            self.assertNotIn(secret, result.stdout + result.stderr)
            for path in self.directory.rglob("*.json*"):
                self.assertNotIn(secret, path.read_text())
        return result

    def state(self):
        return cli.read_json(self.api.manifest)

    def writes(self, path):
        return [item for item in self.api.requests if item[0] == "POST" and item[1] == path]

    def test_plan_is_offline_and_does_not_write(self):
        result = self.call("plan", credentials=False)
        self.assertEqual(json.loads(result.stdout)["transactions"], 3)
        self.assertFalse(self.api.requests)
        self.assertFalse(self.api.manifest.exists())

    def test_seed_then_read_only_verify_and_resume_never_duplicate_writes(self):
        self.call("seed")
        state = self.state()
        self.assertEqual(state["stage"], "completed")
        self.assertEqual(state["tinkUserId"], "tink-user-1")
        self.assertEqual(len(state["transactionIds"]), 3)
        self.assertTrue(self.api.checkpoint_seen)
        self.assertEqual(len(self.writes("/data/v2/connector/accounts")), 1)
        self.assertEqual(len(self.writes("/data/v2/connector/transactions")), 1)
        body = self.writes("/data/v2/connector/transactions")[0][2]
        tx = body["users"][0]["accounts"][0]["transactions"][0]
        self.assertIsInstance(tx["amount"], Decimal)
        self.assertEqual(tx["amount"], Decimal("-49.99"))
        self.assertEqual(tx["bookedDateTime"], "2026-10-20T10:00:00.000Z")
        self.assertEqual(tx["reference"], tx["externalTransactionId"])
        self.assertTrue(any(request[4].get("pageToken") for request in self.api.requests))
        self.call("verify")
        self.call("resume")
        for path in ("/api/v1/user/create", "/data/v2/connector/accounts", "/data/v2/connector/transactions"):
            self.assertEqual(len(self.writes(path)), 1)
        self.call("seed", ok=False)

    def test_verify_hr_adds_resource_ids_without_remote_writes(self):
        self.call("seed")
        result = self.call("verify", "--hr")
        summary, readable = result.stdout.split("\n", 1)
        self.assertEqual(json.loads(summary)["stage"], "completed")
        self.assertIn("user - created, verified - tink-user-1", readable)
        for resource_id in (*self.state()["accountIds"].values(), *self.state()["transactionIds"].values()):
            self.assertIn(resource_id, readable)
        self.assertIn("operation - processed (manifest) - operation-1", readable)
        self.assertIn("balances unchecked: creditLimit", readable)
        for path in ("/api/v1/user/create", "/data/v2/connector/accounts", "/data/v2/connector/transactions"):
            self.assertEqual(len(self.writes(path)), 1)
        plain = self.call("verify", "--hr=False")
        self.assertEqual(json.loads(plain.stdout)["stage"], "completed")

    def test_verify_hr_does_not_claim_unknown_operation_processed(self):
        self.api.mode = "lost_transactions"
        self.call("seed", ok=False)
        result = self.call("verify", "--hr")
        self.assertIn("operation - unknown (no confirmed operationId)", result.stdout)
        self.assertNotIn("operation - processed", result.stdout)
        self.assertEqual(self.state()["transactionsStatus"], "submitting")

    def test_failed_verify_hr_does_not_print_success_summary(self):
        self.call("seed")
        self.api.mode = "wrong_amount"
        result = self.call("verify", "--hr", ok=False)
        self.assertNotIn("created, verified", result.stdout)
        self.assertNotIn("Verification: verified", result.stdout)

    def test_fire_short_resume_and_verify_use_run_id(self):
        self.call("seed")
        with patch.object(cli, "DEFAULT_RUNS", self.directory), patch.dict(os.environ, Tink__ClientId=CLIENT_ID, Tink__ClientSecret=SECRET):
            for command in ("resume", "verify"):
                with self.subTest(command=command), redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                    result = cli.main([command, "case", "--base-url", self.api.base_url, "--wait-timeout", "0.15"])
                self.assertEqual(result, 0)
        self.assertEqual(len(self.writes("/data/v2/connector/transactions")), 1)

    def test_account_list_verification_avoids_forbidden_detailed_balances(self):
        self.call("seed")
        self.call("resume")
        self.call("verify")
        self.assertFalse(any(item[1].endswith("/balances") for item in self.api.requests))
        self.assertFalse(any("accounts.balances:readonly" in item[3]["scope"][0]
                             for item in self.writes("/api/v1/oauth/authorization-grant")))
        result = next(iter(self.state()["accountVerification"].values()))
        self.assertEqual(result["verifiedBalances"], ["bookedBalance", "availableBalanceIncludingCredit"])
        self.assertEqual(result["uncheckedBalances"], ["creditLimit"])
        self.assertEqual(len(self.writes("/data/v2/connector/transactions")), 1)

    def test_missing_list_balances_are_explicitly_unchecked(self):
        self.api.mode = "missing_balances"
        self.call("seed")
        result = next(iter(self.state()["accountVerification"].values()))
        self.assertEqual(result["verifiedBalances"], [])
        self.assertEqual(result["uncheckedBalances"], ["availableBalanceIncludingCredit", "bookedBalance", "creditLimit"])

    def test_mismatching_visible_balances_block_transaction_ingest(self):
        for mode in ("wrong_booked", "wrong_available", "wrong_balance_currency"):
            with self.subTest(mode=mode):
                self.api.mode = mode
                self.api.manifest = self.directory / mode / "manifest.json"
                self.call("seed", "--run-id", mode, ok=False)
                self.assertFalse(self.writes("/data/v2/connector/transactions"))
                self.assertEqual(cli.read_json(self.api.manifest)["lastError"]["code"], "accounts_visibility_timeout")

    def test_lost_create_response_recovers_original_user(self):
        self.api.mode = "lost_user"
        self.call("seed")
        self.assertEqual(self.state()["tinkUserId"], "tink-user-1")
        self.assertEqual(len(self.writes("/api/v1/user/create")), 1)

    def test_write_timeout_keeps_user_id_and_does_not_retry(self):
        self.api.mode = "slow_accounts"
        self.call("seed", "--http-timeout", "0.05", ok=False)
        self.assertEqual(self.state()["lastError"]["code"], "http_timeout")
        self.assertEqual(self.state()["tinkUserId"], "tink-user-1")
        self.api.mode = None
        self.call("resume")
        self.assertEqual(len(self.writes("/data/v2/connector/accounts")), 1)

    def test_missing_operation_id_does_not_replay_ingest(self):
        self.api.mode = "missing_operation"
        self.call("seed", ok=False)
        self.assertEqual(self.state()["lastError"]["code"], "missing_operation_id")
        self.call("resume", ok=False)
        self.assertEqual(len(self.writes("/data/v2/connector/transactions")), 1)

    def test_lost_accounts_response_resumes_by_read_not_repost(self):
        self.api.mode = "lost_accounts"
        self.call("seed", ok=False)
        self.assertEqual(self.state()["tinkUserId"], "tink-user-1")
        self.call("resume")
        self.assertEqual(len(self.writes("/data/v2/connector/accounts")), 1)

    def test_lost_transaction_response_stops_replay_but_allows_read_verification(self):
        self.api.mode = "lost_transactions"
        self.call("seed", ok=False)
        self.call("resume", ok=False)
        self.assertEqual(self.state()["lastError"]["code"], "unknown_transaction_write_requires_review")
        self.call("verify")
        self.assertEqual(self.state()["transactionsStatus"], "submitting")
        self.assertEqual(self.state()["lastVerification"]["status"], "verified")
        self.assertEqual(len(self.writes("/data/v2/connector/transactions")), 1)

    def test_operation_exception_even_when_processed_is_failure(self):
        self.api.mode = "operation_exception"
        self.call("seed", ok=False)
        state = self.state()
        self.assertEqual(state["lastError"]["code"], "operation_failed")
        self.assertEqual(state["operationIssues"][0]["reasons"], ["MISSING_TRANSACTION_AMOUNT"])
        self.assertEqual(state["operationId"], "operation-1")
        self.assertFalse(state["transactionIds"])

    def test_failed_operation_is_failure(self):
        self.api.mode = "failed"
        self.call("seed", ok=False)
        self.assertEqual(self.state()["lastError"]["code"], "operation_failed")

    def test_pending_operation_timeout_is_resumable_with_same_operation(self):
        self.api.mode = "processing"
        self.call("seed", ok=False)
        state = self.state()
        self.assertEqual(state["lastError"]["code"], "operation_timeout")
        self.api.mode = None
        self.call("resume")
        self.assertEqual(self.state()["externalOperationId"], state["externalOperationId"])
        self.assertEqual(len(self.writes("/data/v2/connector/transactions")), 1)

    def test_account_partial_failure_stops_transactions(self):
        self.api.mode = "account_exception"
        self.call("seed", ok=False)
        self.assertEqual(self.state()["accountIssues"][0]["reasons"], ["INVALID_BALANCE"])
        self.assertFalse(self.writes("/data/v2/connector/transactions"))

    def test_400_401_403_are_safe_and_not_replayed(self):
        # Separate runs preserve the uncertain-write rule for each failure.
        for status in ("400", "401", "403"):
            with self.subTest(status=status):
                self.api.mode = status
                self.api.manifest = self.directory / ("http" + status) / "manifest.json"
                self.call("seed", "--run-id", "http" + status, ok=False)
                state = cli.read_json(self.api.manifest)
                self.assertEqual(state["lastError"]["status"], int(status))
                self.assertEqual(state["tinkUserId"], "tink-user-1")
                self.assertFalse(self.writes("/data/v2/connector/transactions"))

    def test_expired_user_token_is_replaced_once_for_safe_read(self):
        self.api.mode = "read_401"
        self.call("seed")
        grants = self.writes("/api/v1/oauth/authorization-grant")
        self.assertEqual(sum(item[3]["scope"] == ["accounts:read,transactions:read"] for item in grants), 2)

    def test_transient_read_retries_without_replaying_writes(self):
        self.api.mode = "read_429"
        self.call("seed", "--wait-timeout", "2")
        self.assertEqual(len(self.writes("/data/v2/connector/accounts")), 1)

    def test_dates_can_be_returned_without_beta_timestamps(self):
        self.api.mode = "date_only"
        self.call("seed")

    def test_existing_test_user_is_verified_without_create(self):
        self.api.user = {"id": "tink-user-1", "externalUserId": "4money-test-existing"}
        self.call("seed", "--existing-external-user-id", "4money-test-existing")
        self.assertFalse(self.writes("/api/v1/user/create"))
        result = self.call("verify", "--hr")
        self.assertIn("user - existing, verified - tink-user-1", result.stdout)

    def test_non_test_user_is_rejected_before_network(self):
        self.call("seed", "--existing-external-user-id", "ordinary-user", ok=False)
        self.assertFalse(self.api.requests)

    def test_precision_multiple_accounts_and_pagination(self):
        preset = cli.read_json(cli.PRESETS / "basic-pln.json")
        for index in range(2):
            account = copy.deepcopy(preset["accounts"][0])
            account["key"] = "extra" + str(index)
            preset["accounts"].append(account)
        preset["accounts"][0]["transactions"][0]["amount"] = "999999999999999999.99"
        source = self.directory / "preset.json"
        source.write_text(cli.encode(preset))
        self.call("seed", "--preset", str(source))
        self.assertEqual(len(self.state()["accountIds"]), 3)
        self.assertEqual(len(self.state()["transactionIds"]), 9)

    def test_modified_manifest_refuses_network(self):
        self.call("seed")
        state = self.state()
        state["accounts"][0]["transactions"][0]["amount"] = Decimal("100")
        cli.atomic_write(self.api.manifest, state)
        count = len(self.api.requests)
        self.call("resume", ok=False)
        self.assertEqual(len(self.api.requests), count)

    def test_lock_refuses_second_writer(self):
        self.call("seed")
        with FileLock(str(self.api.manifest.parent / "run.lock")):
            count = len(self.api.requests)
            result = self.call("resume", ok=False)
            self.assertIn("run_locked", result.stderr)
            self.assertEqual(len(self.api.requests), count)

    def test_wrong_amount_is_not_a_successful_verification(self):
        self.call("seed")
        self.api.mode = "wrong_amount"
        self.call("verify", ok=False)
        self.assertEqual(self.state()["lastError"]["code"], "transactions_verification_timeout")


class LocalTests(unittest.TestCase):
    def test_deterministic_fixtures_and_decimal_serialization(self):
        preset = cli.validate_preset(cli.read_json(cli.PRESETS / "basic-pln.json"))
        first = cli.materialize(preset, "same", date(2026, 10, 23))
        self.assertEqual(first, cli.materialize(preset, "same", date(2026, 10, 23)))
        self.assertNotEqual(first, cli.materialize(preset, "other", date(2026, 10, 23)))
        self.assertIn('"amount": -49.99', cli.encode(first))

    def test_invalid_money_and_extra_fields_rejected(self):
        for amount in (1.2, "NaN", "1.001", "1e2"):
            preset = cli.read_json(cli.PRESETS / "basic-pln.json")
            preset["accounts"][0]["transactions"][0]["amount"] = amount
            with self.subTest(amount=amount), self.assertRaises(cli.ConnectorError):
                cli.validate_preset(preset)
        preset = cli.read_json(cli.PRESETS / "basic-pln.json")
        preset["client_secret"] = "placeholder"
        with self.assertRaises(cli.ConnectorError):
            cli.validate_preset(preset)

    def test_atomic_failure_preserves_previous_manifest_and_prints_recovery_id(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            cli.atomic_write(path, {"previous": True})
            store = cli.RunStore(path, {"runId": "case", "externalUserId": "4money-test-case", "tinkUserId": "saved-user"})
            with patch.object(cli.os, "replace", side_effect=OSError), patch("sys.stderr") as stderr:
                with self.assertRaises(cli.ConnectorError):
                    store.save()
                self.assertIn("saved-user", str(stderr.write.call_args_list))
            self.assertEqual(cli.read_json(path), {"previous": True})
            self.assertFalse(list(Path(directory).glob("*.tmp")))

    def test_failed_event_log_does_not_discard_checkpoint(self):
        with tempfile.TemporaryDirectory() as directory:
            store = cli.RunStore(Path(directory) / "manifest.json", {"runId": "case"})
            with patch.object(Path, "open", side_effect=OSError), patch("sys.stderr") as stderr:
                store.log("checkpoint", tinkUserId="saved-user")
                self.assertIn("saved-user", str(stderr.write.call_args_list))
                self.assertIn("event_log_write_failed", str(stderr.write.call_args_list))

    def test_fire_invalid_arguments_fail_before_network(self):
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()), patch.object(cli.TinkClient, "request") as request:
            self.assertEqual(cli.main(["resume", "../outside"]), 1)
            self.assertEqual(cli.main(["resume", "case", "--manifest", "/tmp/unused-manifest.json"]), 1)
            self.assertEqual(cli.main(["resume"]), 1)
            self.assertEqual(cli.main(["plan", "--as-of-date", "invalid-date"]), 1)
            with self.assertRaises(fire.core.FireExit):
                cli.main(["seed", "--unknown-option", "value"])
            request.assert_not_called()

    def test_client_token_cache_reuses_token_and_expires_with_margin(self):
        client = cli.TinkClient("https://api.tink.com", CLIENT_ID, SECRET, None)
        with patch.object(client, "request", return_value={"access_token": TOKEN, "expires_in": 100}) as request:
            with patch.object(cli.time, "monotonic", return_value=0):
                self.assertEqual(client.client_token("accounts:write"), TOKEN)
            with patch.object(cli.time, "monotonic", return_value=89):
                client.client_token("accounts:write")
                self.assertEqual(request.call_count, 1)
            with patch.object(cli.time, "monotonic", return_value=91):
                client.client_token("accounts:write")
                self.assertEqual(request.call_count, 2)


if __name__ == "__main__":
    unittest.main()
