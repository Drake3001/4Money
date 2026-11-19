# Banking connection flow

Banking coordinates durable connection attempts, local connection reads, and the
Tink Transactions Continuous Access adapter. Account and transaction import is handled by the refresh worker described below;
webhooks, consent renewal, and disconnection remain separate stages.

| Method | Path | Success |
| --- | --- | --- |
| POST | `/api/banking/connection-attempts` | 201, attempt ID, authorization URL, expiry, Location |
| GET | `/api/banking/connection-attempts/{attemptId}` | 200, attempt state |
| GET | `/api/banking/connections` | 200, local connections (possibly empty) |
| GET | `/api/banking/consents` | 200, current provider consents matched to local connections |
| GET | `/api/banking/connections/{connectionId}` | 200, local connection |
| POST | `/api/banking/refreshes` | 202, durable refresh job, Location |
| GET | `/api/banking/refreshes/{refreshId}` | 200, owner-scoped job and account results |
| GET | `/api/banking/tink/callback` | 200, final attempt state |
| GET | `/api/banking/accounts` | 200, paginated local accounts |
| GET | `/api/banking/accounts/{accountId}` | 200, owned local account |
| GET | `/api/banking/transactions` | 200, paginated local transactions |
| GET | `/api/banking/transactions/{transactionId}` | 200, owned local transaction |

`POST /api/integrations/tink/link` was removed. Clients must use the Banking
endpoint and its response contract. A URL is no longer generated independently
of a persisted attempt.

## Responsibilities and persistence

### Local account and transaction reads

`IBankingReadService` is a scoped service registered by the API host for direct
use by other modules through DI. It reads PostgreSQL only: it neither contacts
Tink nor provisions identities, starts refreshes, creates expenses or writes data.
`BankingReadController` exposes these operations through separate HTTP DTOs as
described below. Existing connection/refresh HTTP contracts remain unchanged.

| Method | Arguments besides required local `userId` and optional cancellation token | Successful value inside `BankingResult<T>` |
| --- | --- | --- |
| `GetAccountsAsync` | `BankAccountReadQuery` | `PagedResult<BankAccountDetails>` |
| `GetAccountAsync` | local `accountId` | `BankAccountDetails` |
| `GetTransactionsAsync` | `BankTransactionReadQuery` | `PagedResult<BankTransactionDetails>` |
| `GetTransactionAsync` | local `transactionId` | `BankTransactionDetails` |

The interface and implementation live in `Services/Reads`, with immutable snapshot records, query classes
and sort enums in `Services/Contracts`. `BankAccountDetails` contains local ID,
name, nullable number/IBAN/institution/type, currency, closed flag and nullable
`LastSuccessfulSyncAt`/`TransactionsSyncedThrough`. `BankTransactionDetails`
contains local ID and account ID, signed decimal amount, currency, DateOnly date,
nullable description and the stored status string. No EF entities, navigations,
Tink user/account/transaction IDs or credentials cross this contract.

Callers must supply a trusted local user ID; a future HTTP adapter must derive it
from validated authentication, never accept a caller-selected owner. Each
operation checks the local User profile: missing users return `NotFound`, inactive
users `Forbidden`. Account/transaction details are owner-scoped in SQL; a missing
or foreign resource returns the same `NotFound`. An explicit missing/foreign
`BankAccountId` transaction filter also returns `NotFound`; an owned account
without transactions returns an empty page. No Tink mapping is required.

Reads include the user's entire local history: closed accounts, legacy rows
without Tink identity and rows from previous Tink mappings. Account filters are
name, currency, type and nullable `IsClosed` (null includes both states).
Transaction filters are account ID, inclusive dates, inclusive signed amount
bounds, currency, description, status and nullable assignment flags (described
below). No status filter includes every stored status, including test/legacy Pending and Cancelled; consumers such as Expense
can explicitly request `Booked`. Read amounts are not converted, rounded or
treated as positive expense amounts.

Name/description use case-insensitive substring matching. Currency is trimmed
and normalized to uppercase; type/status use exact matching after trimming.
Blank strings are ignored. Queries have no owner field and do not expose
repository types. The service copies filters into new owner-scoped queries.

Pagination reuses Shared defaults: page 1, size 20, maximum size 100. Accounts
default to name ascending, with currency/closed flag as other sort choices.
Transactions default to date descending, with amount/status/currency as other
choices. All sorts break ties by local ID ascending. Pages past the end are
empty but retain the total count. Each database query reads current committed
data; a multi-page read is not a frozen snapshot of concurrent imports.

Empty GUIDs, null queries, invalid pagination/offsets, unknown sort enums and
reversed date/amount ranges throw standard `ArgumentException` subclasses before
database reads. Database failures and cancellation propagate to the caller.
No changes to `BankingErrorCode` are needed. Reads use `AsNoTracking`; existing
unscoped repository detail methods remain available to existing code but are not
used by this service. Ownership enforcement belongs to the service, not optional
repository query filters.

### Transaction assignments — 2026-11-03

Banking owns `banking.bank_transaction_assignment`: `bank_transaction_id` is the
primary key and a NoAction FK to the local transaction, `consumer_type` is a
required integer (`Expense=1`, `GroupSettlement=2`, enforced by CHECK), and
`consumer_id` is a required UUID. The polymorphic consumer ID has no FK to other
modules. One transaction can have at most one assignment in this registry.
`AddBankTransactionAssignments` creates an empty table without backfilling existing
links. Apply it before running the local seed, which registers synthetic links.

`settlementAssignment` and `expenseAssignment` are independent nullable boolean
filters, combined with AND. Omission ignores that assignment type, true requires
it and false excludes it. Both false return free transactions; both true return
an empty page with 200 because a transaction cannot have both types. Without
either flag, the list retains its existing behavior. Filtering uses SQL EXISTS /
NOT EXISTS against the registry before counting and pagination, within the
existing owner scope. Detail responses and list item fields are unchanged.

#### Internal assignment service

`IBankTransactionAssignmentService` / `BankTransactionAssignmentService` live in
`Services/Assignments`; their repository lives in
`Repositories/BankTransactionAssignmentRepository`. Both are scoped in host DI.
`BankTransactionAssignmentDetails` is an immutable snapshot with transaction ID,
consumer type and consumer ID. No EF entities cross the service contract.

| Method | Result |
| --- | --- |
| `GetAsync(bankTransactionId)` | Snapshot or null when unassigned, including a missing transaction; no explicit transaction required |
| `AssignAsync(bankTransactionId, consumerType, consumerId)` | `BankingResult<BankTransactionAssignmentDetails>`: assigns a free transaction or succeeds for an identical assignment; another consumer returns `Conflict`; a missing transaction returns `NotFound` |
| `ReleaseAsync(bankTransactionId, consumerType, consumerId)` | `BankingResult<bool>`: true when an exact match is removed, false when already free; another consumer returns `Conflict`; a missing transaction returns `NotFound` |

All methods support cancellation. Empty GUIDs and undefined consumer types throw
`ArgumentException` subclasses before repository access. This is an internal
contract for trusted services: user activity, ownership, currency and consumer
existence must be validated by the calling business flow.

Writes require an explicit transaction on the same scoped `BankingDbContext`;
without it they throw `InvalidOperationException` before database access. The
caller must begin/enlist the transaction, inspect results, and commit or roll back
its whole operation. The service never starts, commits or rolls back a
transaction. It does not call `SaveChangesAsync`, flush pending tracked changes,
or clear the change tracker. Repository writes execute immediately in the
caller's transaction; reads use `AsNoTracking`.

Each mutation first locks the bank transaction row with `SELECT ... FOR UPDATE`
until the caller ends its transaction. Insert uses parameterized `ON CONFLICT
(bank_transaction_id) DO NOTHING`, then reads and compares the assignment;
expected conflicts do not abort the transaction. Release checks the consumer and
uses a conditional delete on all three fields. This serializes service writes
for a given transaction, including repeated assignments and concurrent releases.
Database failures (including deadlocks/serialization failures at stronger
isolation levels) and cancellation propagate; the caller owns rollback and any
retry of its entire unit of work. A free read does not reserve a transaction;
an assignment result becomes durable only when the caller commits.

There is no HTTP write API, overwrite/reassignment operation, or integration with
Banking/Expense/Grouping workflows. Banking retains its separate connection;
future coordinators must enlist its context in the business transaction before
using the service. Those flows must maintain the registry atomically with their
source record. Until then the filters reflect registry contents, not live joins
to consumers. Banking has no dependency on Expense or Grouping. The registry
alone does not enforce consistency with their existing source columns.

### HTTP account and transaction reads

All four read endpoints require the existing User Bearer JWT and use
`ICurrentUser` from `ZPI4M.Shared.CurrentUser` to supply the owner. No `userId` query parameter selects
another user's data. Reads work without a Tink mapping or configuration and do
not start synchronization. Responses from the controller use `Cache-Control:
no-store`. Closed accounts, historical mappings and all stored transaction
statuses remain visible unless filtered.

List query parameters (optional):

| Endpoint | Filters | Sorting |
| --- | --- | --- |
| `/accounts` | `name`, `currencyCode`, `type`, `isClosed` | `sortBy=name` (default), `currencyCode`, `isClosed`; `descending=false` by default |
| `/transactions` | `bankAccountId`, `dateFrom`, `dateTo`, `minAmount`, `maxAmount`, `currencyCode`, `description`, `status`, `settlementAssignment`, `expenseAssignment` | `sortBy=date` (default), `amount`, `status`, `currencyCode`; `descending=true` by default |

Both lists use `pageNumber=1`, `pageSize=20` (maximum 100). HTTP request DTOs
inherit pagination fields and validation from `ZPI4M.Shared.Pagination.PagedQuery`;
Banking adds validation for sorting and filters. `GetOffset()` retains exception-based
validation for service and repository callers. Sort names are
case-insensitive; numeric and unknown sort values are rejected. Equal sort keys
use local ID ascending. Filters retain the service semantics above: date/amount
bounds are inclusive, names/descriptions match case-insensitive substrings,
currency is normalized, type/status match exactly after trimming. Dates use
`YYYY-MM-DD`, query amounts use invariant decimal notation with a dot. Null
`isClosed` includes all accounts. Blank text filters are ignored.

Responses are camelCase JSON. Account fields match `BankAccountDetails`;
transaction fields match `BankTransactionDetails` except that HTTP `amount` is
**a string with exactly two fractional digits**, e.g. `"-123.45"`, `"0.00"`,
`"9999999999999999.99"`. Do not convert amounts to JavaScript `number` when exact
arithmetic matters. No currency conversion or sign change takes place. Dates
are `YYYY-MM-DD`; timestamps are ISO 8601 and nullable fields are retained as
JSON null. Provider identity, credentials and EF entities are never exposed.

Lists use the shared envelope: `items`, `totalCount`, `pageNumber`, `pageSize`,
`totalPages`, `hasPreviousPage`, `hasNextPage`. An empty list or a page beyond the
end returns 200 with an empty `items` array and the actual total count.

Examples:

```http
GET /api/banking/transactions?settlementAssignment=false&expenseAssignment=false
GET /api/banking/accounts?currencyCode=PLN&isClosed=false&pageSize=20
GET /api/banking/transactions?bankAccountId=10000000-0000-0000-0000-000000000010&dateFrom=2026-01-01&dateTo=2026-01-31&minAmount=-200.00&maxAmount=0.00&status=Booked&sortBy=date&descending=true
```

Input validation returns 400 `ValidationProblemDetails` with field errors for
bad types, empty GUIDs, invalid pagination/offsets, sort values and reversed
ranges. A malformed detail GUID does not match the `:guid` route and returns
404; an all-zero GUID returns 400. Missing/foreign details and an explicit
missing/foreign `bankAccountId` filter return identical 404 `BANKING_NOT_FOUND`.
Inactive users return 403 `BANKING_FORBIDDEN` from the service, while production
JWT validation can reject an unavailable account/session earlier with 401.
Database failures map to 503 `BANKING_TEMPORARILY_UNAVAILABLE` without diagnostic
details. Authentication/model-binding responses retain the host's standard
ProblemDetails conventions; they need not contain a Banking `code`.
Cancellation propagates and programming errors are not reclassified as 400.

Response types and validation attributes are included in the host's generated
Development OpenAPI document. Runnable synthetic examples are in folder 80 of
the Banking Postman collection; see [Postman instructions](../../../../../notes/TINK_POSTMAN.md#odczyt-lokalnych-rachunków-i-transakcji).

### Connections

`Services/Connections` contains `BankingConnectionService`, its interfaces and
`BankingConnectionOptions`. `BankingConnectionService` implements `IBankingConnectionService` and
`IBankingCallbackService`. Repositories own database operations; Tink implements
`ITinkConnectionGateway`. The User module exposes profile reads and serialized
initialization of the existing `User.TinkUserId`. There is no duplicate mapping
or dependency from User to Banking.

Connection persistence uses `IDbContextFactory<BankingDbContext>` with a scoped
factory registration. Each repository operation creates and disposes its own
context, including reads that expire attempts. Writes commit independently of
caller transactions and neither save nor detach unrelated changes in the scoped
Banking context. Callback completion still locks the attempt and atomically
commits its result and connection. Read results are untracked. The scoped context
remains available for reads, refresh processing and caller-owned assignment
transactions; their persistence contracts are unchanged.

Each recognized user's start request saves a `BankConnectionAttempt` before
calling Tink. Inactive users and integration failures leave failed attempts.
An unknown user or unavailable database cannot produce a durable attempt.
One row retains the current state and result of each attempt, not a separate
history of transitions. Attempts live for 15 minutes by default (configurable
through `Banking:AttemptLifetimeMinutes`, range 1–60). This is the attempt's
expiry, not a promise about the lifetime of the Tink authorization code.
Expiry is persisted on reads/callbacks. An interrupted process can leave a pending
row until it is accessed and expired. Cancellation tries to save failure using a
separate bounded timeout.

The consolidated `20261026215244_AddBanking` migration creates the connection
and refresh tables, their indexes and foreign keys, and changes account columns
and transaction constraints as described below. New timestamps use UTC
`timestamp with time zone`. Baseline SQL must already be applied before migrations;
see the [development instructions](../../../../../agent-context/development.md).
This migration replaces this branch's `AddBankConnections` and `AddBankRefreshes`.
Databases that already applied either old migration need recreation or a separately
planned transition; see the development instructions above. Generating the migration
does not apply it to a developer database.

## Tink user initialization on connection start — 2026-11-07

Registration, email confirmation and login do not call Tink. The client starts
bank linking with authenticated `POST /api/banking/connection-attempts`, without
an additional initialization request. The standalone `/api/banking/tink-user`
endpoint has been removed; existing clients must use connection start instead.

Connection start reuses an existing `TinkUserId` or creates/recovers and persists
one through User's `EnsureTinkUserAsync` before generating the authorization link.
The User boundary serializes initialization per user with a PostgreSQL advisory
lock. The adapter uses a stable `external_user_id` to recover a remote identity
after a lost response or local rollback. User does not depend on Banking.

Provider errors, network failures, timeouts and invalid responses return safe
503 `BANKING_TEMPORARILY_UNAVAILABLE` with `attemptId`. The attempt is marked
`Failed` with `TINK_UNAVAILABLE`; failure persistence is best effort, and an
unmodified Pending attempt expires on access. No invalid mapping is persisted.
A mapping already saved before a link failure is retained. No bank connection
is created until successful callback completion.

After 503, retry connection start with the same session. Each request creates
a new attempt and reuses or recovers the same Tink identity. Registration/login
remain intact. Request cancellation propagates and attempts are marked failed
best effort. Configuration gates, including `ContinuousAccessVerified`, remain
unchanged; no schema migration is required.

## Identity and callback

User operations require validated 4Money authentication and scope all resource
reads to the local user. Foreign and missing resources both return 404. The host
registers the shared `HttpCurrentUser`, which reads `sub` from a validated User JWT
and also supports `ClaimTypes.NameIdentifier`. The User module configures JWT
validation and checks that the session and account are active. Authentication
middleware runs before authorization. No request header or development fallback
establishes a production identity.

The callback is anonymous because a browser redirect need not carry a Bearer
token. It requires the random 256-bit `state` sent when starting the journey.
Only its SHA-256 hash is persisted. The SDK's `credentials_id` is verified using
a fresh user token for the attempt's Permanent User, including the returned owner,
ID, provider and successful `UPDATED` credential state. Unsupported/not-ready
states stay pending and return 503, allowing a callback retry before expiry.
No parameter from the callback alone establishes ownership.

The callback accepts `state`, `credentials_id`, `error`, and `error_reason` and
ignores additional SDK metadata. Ambiguous repeated query parameters, malformed
inputs and foreign credentials are rejected. Errors store allowlisted 4Money
codes, never arbitrary provider messages. Completion checks state and expiry
under a database row lock and atomically saves the connection and attempt result.
A repeat callback returns 409. Multiple connections to the same provider are
allowed; a credential already assigned to another owner is not reassigned.

Callback responses contain the local attempt result, require no frontend, and
set `Cache-Control: no-store` and `Referrer-Policy: no-referrer`. Configure the
registered Tink redirect URI to point at this backend endpoint. Do not log query
strings or authorization URLs at application/proxy/observability boundaries.

## Errors and availability

All connection controller actions explicitly set `Cache-Control: no-store`, as do
the callback and existing account/transaction/refresh controllers. Authentication
and routing failures produced before controller execution retain host behavior.
Connection and callback database exceptions (`DbException` / `DbUpdateException`)
map to safe 503 `application/problem+json` responses with code
`BANKING_TEMPORARILY_UNAVAILABLE` and `attemptId: null` when no service result is
available. Callback availability failures retain `Retry-After: 5` and
`Referrer-Policy: no-referrer`. Cancellation and programming errors propagate;
provider-returned failures keep their existing codes and known attempt IDs.

Application errors return ProblemDetails with a stable `code`: 401
`BANKING_UNAUTHENTICATED`, 403 `BANKING_FORBIDDEN`, 404 `BANKING_NOT_FOUND`,
409 `BANKING_CONFLICT`, 400 `BANKING_INVALID_CALLBACK` (callback only), and 503
`BANKING_TEMPORARILY_UNAVAILABLE`. A start failure after persistence includes
`attemptId`. Errors raised by authentication middleware may have a different
body. Local reads never contact Tink and remain possible without valid Tink
configuration. Configuration is validated when used, not at host startup.

After verified completion a connection is `CONNECTED` with synchronization
`NOT_STARTED`; no data import or readiness is claimed. Unknown provider display
name, consent expiry, and synchronization timestamps remain null.

## Reading provider consents

`GET /api/banking/consents` reads the authenticated user's current consent list
from Tink on demand. It accepts no user ID. The response is a JSON array of
`connectionId` (local 4Money ID), `providerStatus`, `sessionExpiresAt` (nullable
UTC timestamp), and `sessionExtendable` (nullable boolean). Provider, credential,
user and account identifiers are not exposed. Unknown nonempty provider statuses
are preserved; neither a status nor an expiry timestamp alone guarantees access.

The service reads the User module's existing profile and local connections,
then matches `credentialsId` against connections owned by that user with the
same current `TinkUserId`. Missing users return 404, inactive users 403. Missing
Tink mapping or matching local connections returns `200 []` without calling
Tink or creating a user. Empty/unmatched provider results also return `[]`;
they do not change connection status or imply revocation.

The adapter requests a user token with only `provider-consents:read` through
the existing authorization grant flow, then calls `GET /api/v1/provider-consents`.
The app must be entitled to this scope in Console. No change to Link delegated
scopes is required. Optional fields remain null when absent; `sessionExpiryDate`
is converted from Unix milliseconds. Upstream/transport/configuration errors
and malformed responses return safe 503 ProblemDetails, never partial results.

This read has `Cache-Control: no-store` and retains the existing identity guard
and `ContinuousAccessVerified` gate. Neither user tokens nor results are cached
or persisted. It does not update local `ConsentExpiresAt`, invoke a refresh or
renew consent, and it is not part of callback completion. No schema change is
needed. Existing local connection reads still operate independently of Tink.

## Repository organization

All Banking repositories use `Repositories/<Name>Repository/` with a matching
namespace. `BankingConnectionRepository/` contains the shared implementation and
both `IBankConnectionRepository` and `IBankConnectionAttemptRepository` interfaces.
`BankRefreshRepository/` contains refresh persistence and its supporting types.
Account, transaction and assignment repositories retain their existing folders.

## Tink integration structure

```text
Tink/
├── TinkServiceCollectionExtensions.cs
├── ITinkConnectionGateway.cs
├── TinkConnectionGateway.cs
├── Configuration/
│   └── TinkOptions.cs
├── Authentication/
│   ├── TinkClientTokenProvider.cs
│   └── TinkAuthorizationService.cs
├── Http/
│   ├── TinkHttp.cs
│   └── TinkApiException.cs
└── Link/
    └── TinkLinkUrlBuilder.cs
```

`AddTink(...)` registers the adapter. It handles provider-specific tokens,
provisioning, authorization, link generation and verification. Attempts and local
connection state belong to Banking services and repositories outside `Tink`.
Private transport response types stay with their consumer.

Sandbox entitlement and a real end-to-end run are **not verified** by repository
tests. Real calls are disabled until `Tink:ContinuousAccessVerified` is explicitly
set after the deployment checks in [Tink setup](../../../../../notes/TINK.md).

## Refresh on demand

`POST /api/banking/refreshes` accepts no body and returns `202 Accepted` with
`refreshId`, `status`, `createdAt` and a `Location` pointing to
`GET /api/banking/refreshes/{refreshId}`. Authentication uses the existing User
JWT. The existing `User.TinkUserId` is required: missing mapping returns 409
`BANKING_TINK_USER_REQUIRED`, without provisioning a Tink user. One active job
per local user is enforced in PostgreSQL; repeated POSTs return that job.
Foreign and absent jobs both return 404. Responses are not cached.

The hosted `BankRefreshWorker` processes durable PostgreSQL jobs. Statuses are
`QUEUED`, `RUNNING`, `SUCCEEDED`, `PARTIALLY_SUCCEEDED`, `FAILED`. GET returns
creation/start/completion times, account counts, a safe failure code and account
results (`accountId`, status, added/updated transaction counts,
`lastSuccessfulSyncAt`, failure code). Provider IDs, tokens and lease metadata
are private. A successful empty account list completes successfully.

This operation **reads data already in Tink**, including synthetic Connector
accounts. It does not invoke credential refresh, renew consent, create expenses,
store balances or require a local BankConnection. Connection sync statuses are
not changed because accounts need not belong to a credential.

The adapter reuses authorization grants with `user_id` and a fresh user token
with `accounts:read,transactions:read`. It reads every `/data/v2/accounts` page,
then every `/data/v2/transactions` page for each account, using `accountIdIn`,
`statusIn=BOOKED`, `bookedDateGte`, `bookedDateLte`, `pageSize=100`. A 401 renews
the user token once. Repeated page tokens, duplicate IDs and malformed data fail
safely. The existing `ContinuousAccessVerified` gate remains in force.

Account discovery is committed only after the whole list passes validation.
The remote identity is `(TinkUserId, TinkAccountId)`; local IDs are stable.
Metadata is updated and missing accounts for the current Tink mapping are
marked `IsClosed`, preserving history and expense references. Reappearing
accounts reopen. Legacy rows without Tink identity are neither matched by
number nor closed. The old number/institution unique index is removed: masked
account numbers may repeat. Number, IBAN and institution may be null. Currency
comes from booked balance, falling back to available; missing or contradictory
currency fails discovery without closing accounts.

Transaction writes are atomic **per account**. Upsert preserves local transaction
IDs and expense references. Only BOOKED is imported, mapped to the existing
local `Booked` status. Description prefers `original`, then `display`; date
prefers `dates.booked`, then UTC `bookedDateTime`. Exact-number amounts are
converted without floating-point arithmetic or rounding; negative scales and
signed amounts are supported. Values outside `decimal(18,2)` fail the account.
Missing transactions in an incremental window are not deleted. Corrections
older than the configured overlap may require a future explicit historical
resynchronization, which this API does not expose.

New accounts start 30 days before job start; existing accounts start 7 days
before `TransactionsSyncedThrough`, even after a longer gap. Each job freezes
its upper date at first execution and persists each account's window for retries.
`LastSuccessfulSyncAt` records account completion; `TransactionsSyncedThrough`
records the job's initial start. Neither advances after a failed account.
Successful accounts are skipped when a job is retried or recovered.

Configuration is under `Banking:Refresh` (environment prefix
`Banking__Refresh__`): `InitialHistoryDays=30`, `OverlapDays=7`, `MaxAttempts=3`,
`FirstRetrySeconds=30`, `SubsequentRetrySeconds=120`, `AttemptTimeoutMinutes=10`,
`PollSeconds=2`, `LeaseSeconds=120`, `HeartbeatSeconds=30`, `MaxPages=10000`.
Each process runs one job at a time. Database claims use `FOR UPDATE SKIP LOCKED`;
lease heartbeats use a separate scope. Every write checks lease ownership and
expiry. Successful data writes also lock/check user activity and current Tink
mapping. Process shutdown/timeouts leave retryable work; expired leases can be
reclaimed. Network errors, 429 and 5xx retry up to the configured attempt limit,
respecting Retry-After. Data and permission errors do not retry. A failed account
does not roll back other accounts; terminal results reflect partial success.

Apply the `AddBanking` migration using the existing migration procedure.
It handles the baseline SQL constraint as well as an EF-created standalone
index. It also removes the historical `amount > 0` constraint, which is
incompatible with signed Tink debits. A rollback to the previous schema may
fail if imported signed amounts or duplicate masked account numbers violate
its older restrictions; no imported data is silently removed to enable rollback.
Do not apply migrations to a shared/development database as part of tests.
