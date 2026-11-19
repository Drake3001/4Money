# Limits — calendar budgets

## Model and behavior

A UserLimit or GroupLimit is a persistent budget definition. Week=1 means
Monday–Sunday, Month=2 a calendar month, and Year=3 a calendar year, calculated
in Europe/Warsaw using TimeProvider. Custom=4 is a one-off budget with saved inclusive
startDate/endDate.
There is no renewal, successor creation, reset, or future activation of recurring budgets.

Budgets support PLN only. Personal usage sums the owner's expense shares across groups,
including accessible historical expenses; group usage sums group purchases. Categories
restrict usage to their items. Expense owns existing rounding and cent allocation through the SQL aggregation service.
Expense dates (including legacy timestamps) use UTC, matching Reports; Europe/Warsaw
only determines today and the current budget window. Both range boundaries are inclusive.
Calendar usage includes the full calendar window, including spending before creation.
Custom usage always includes its complete saved range, before/during/after the trip.
Previous target amounts are not retained; historical spending belongs to Expense/Reports.

Custom definitions are independent: they may overlap calendar definitions and any other
Custom definitions within the same owner/category/currency scope, including identical ranges.
At most one enabled calendar definition (Week/Month/Year) may exist in each scope.
General and category scopes remain independent; disabled calendar definitions do not reserve a scope.

Creating or enabling a definition never disables another definition or changes its version.
Each matching definition counts the same expense independently and can issue its own alerts.
For example, a three-day trip and a monthly limit both count purchases during the trip;
the monthly limit also counts the rest of that month. Their usage values must not be summed
as if they represented separate purchases. Finished Custom definitions stay enabled until
explicitly disabled, retain live historical usage and never send finished-period alerts.

Owner, category, currency, period, and Custom dates are immutable. Change them by disabling
and creating another definition. Amount edits are immediate, including while disabled,
but a Custom target is frozen after its end date. Finished definitions remain readable,
and expense corrections still update their usage. Enabling a finished definition does not
restart its alerts or unlock the target. No worker marks completion or modifies dates.

## HTTP contract (2026-11-17)

Both `/api/users/me/limits` and `/api/groups/{groupId}/limits` expose:

| Method | Path suffix | Behavior |
| --- | --- | --- |
| GET | collection | Paged definitions; enabled only by default |
| GET | /{limitId} | Definition and live resolved-window usage, also when disabled |
| POST | collection | Create enabled definition |
| PUT | /{limitId} | Edit amount: `{ "amount": "800.00", "version": "UUID" }` |
| PATCH | /{limitId}/enabled | `{ "isEnabled": false, "version": "UUID" }` |

POST requires amount (positive decimal string fitting numeric(18,2)), period, optional
categoryId, and personal currencyCode="PLN". Group currency comes from Grouping;
non-PLN groups receive 400 currencyCode. Custom POST requires finite startDate/endDate
(YYYY-MM-DD), start <= end, and end >= today in Warsaw. Same-day/ongoing/future trips are
valid; entirely past creation is rejected. MinValue/MaxValue sentinel dates are rejected.
There is no maximum duration. Calendar POST accepts omitted/null dates and rejects
non-null dates. Unknown body fields are rejected.
PUT cannot change dates, scope or period.
Mutations return 201/200 with the definition and fresh opaque UUID version.
A valid but stale version gives 409 code=VersionConflict; overlaps give
409 code=OverlappingBudget for conflicting enabled calendar definitions. Finished Custom amount edits give 409 code=FinishedBudgetImmutable. Malformed versions and invalid fields give 400.
Every accepted edit rotates version; alert processing does not change it.

Responses include id, userId/groupId, categoryId, amount, currencyCode, period, setDate,
isEnabled, version, non-null periodStartDate and periodEndDate (calendar-calculated or Custom saved bounds). Details also include usage;
list/create/edit/enable responses omit it. Disabled usage remains live, not frozen.
Usage contains currencyCode, spentAmount, remainingAmount, usedPercentage,
and isExceeded; all usage fields are required and there is no usage status field.
Unsupported budget currencies return 400 currencyCode; an aggregation currency mismatch
is an internal consistency error and never returns partial usage.
Amounts are decimal strings; percentage rounds AwayFromZero to two
places. Exceeded means strictly greater than the target; negative totals are retained.
Expense errors propagate as 400/404/503 rather than zero usage.

Lists use existing pagination (default 20, maximum 100), includeDisabled=false,
optional categoryId/currencyCode/period (1,2,3,4), ordered by setDate then id; descending reverses
both. GET never writes alert history or notifications.

Identity comes from ICurrentUser. Personal definitions are private to their owner.
Current members can read group definitions; only the current member administrator can
write. Archived/inaccessible groups and former members get 404; a member without
administrator permissions gets 403 on writes. Checks occur before mutation validation.

## Alerty w inboxie

Expense create/edit/delete, budget creation, enabled amount edits and re-enabling
trigger evaluation. A background sweep evaluates enabled budgets active today: calendar
budgets use the current calendar window, Custom budgets require start <= today <= end.
Future and finished Custom budgets generate no alerts, including after corrections or
re-enabling. Historical alerts are never backfilled. Each budget has independent alert
state; overlapping budgets can notify for the same expense. A newly created calendar
budget can immediately alert on full-period spending without changing any Custom definition.
LimitsAlerts:WarningThresholdPercent defaults to 80, strictly between 0 and 100.
Warning qualifies at the threshold, exceeded only above target. A direct jump above
target sends exceeded only and marks approaching handled. Corrections, amount edits,
and disable/re-enable never reset handled states within the same period.

Separate user/group alert-state tables key rows by (limit_id, period_start_date), with
ApproachingHandledAt and ExceededNotifiedAt UTC timestamps. Custom uses its saved start
date as the key, deduplicating throughout the trip. Calendar budgets use the current
calendar start. No unbounded/sentinel key remains.
State rows are created only after a notification has active recipients. No recipients
leaves state unhandled so the sweep can retry. Group delivery remains once per budget
period, not once per future/reactivated member; a partially delivered group alert is
not resent. Personal alerts go to the active owner; group alerts to all current active
members. Group alerts reference GroupId, personal alerts neither GroupId nor ExpenseId.

Source mutation, state and inbox share the Grouping transaction coordinator, with
explicit context enlistment. Group writes/sweep use the group lock; personal work uses
ordered owner advisory locks. Expense keeps group-before-owner ordering. All operations
are sequential. Failures roll back together; dispose the failed DI scope.

## Alert sweep

LimitsAlertsSweep options (startup snapshot, restart after changes):

```json
{ "Enabled": true, "RunOnStartup": true, "IntervalSeconds": 60,
  "RunTimeoutSeconds": 300, "MaxDefinitionsPerRun": 100 }
```

Times must be 1..86400 seconds, MaxDefinitionsPerRun 1..10000. Validation applies
when disabled too. Standard overrides include LimitsAlertsSweep__IntervalSeconds.
Enabled=false issues no sweep queries.
The singleton ILimitAlertSweepService maintains an in-memory cursor and requires sequential
calls on each instance; it has no semaphore or other internal concurrency gate. Its production
caller is the worker, which awaits each run to completion before waiting for the next interval.
Timeout cancellation is cooperative: even a slow-to-cancel run must finish before another starts.
Separate application instances remain protected by database locks and unique alert-state keys.
The worker uses TimeProvider for waits and
linked host-shutdown/run-timeout cancellation. Each candidate has a fresh DI scope and
transaction. Inactive personal owners and archived groups are skipped after rechecking
under lock; trusted group totals use IExpenseSystemAggregationService and require an active coordinator;
the caller enlists ExpenseDbContext and validates/locks the group, without impersonating an administrator.

Candidate queries exclude future/finished Custom definitions using the captured Warsaw
date; eligibility is rechecked under lock. Keyset pagination orders personal definitions
then group definitions by UUID, with an
in-memory continuation cursor. A run processes at most MaxDefinitionsPerRun. Empty tails
wrap to the beginning. Failed/skipped/timeout candidates advance the cursor so others
can progress; they are retried after wrapping. Restart resets only the cursor, not alert
history. Multiple instances are protected by the same locks and unique state keys.
Logs contain evaluated/skipped/notified/failed counts and duration, without amounts.
No persistent queue, leader election, period creation, or renewal is needed.

## Migration and rollout

`20261119182900_AddLimits` follows the shared
`20261116211631_NormalizeExpenseCategoriesAndAddDeposit` migration.
Up locks and requires empty legacy group_limit and category_group_limit tables before
DDL. Down requires empty final definition and alert-state tables, restoring the exact
legacy schema including SQL-init constraints. No data is deleted or converted.
Unrelated data and inbox notifications are retained. Custom=4 stores finite inclusive
dates; calendar definitions store null dates. General/category exclusion constraints apply
only to enabled periods 1/2/3, preserving calendar uniqueness while allowing Custom overlaps.
The shared btree_gist extension is created if needed and retained on downgrade.

AddLimits is the only migration introduced by this PR. It replaces the removed
`20261117155636_AddBudgetDefinitions`; do not apply it over that schema.
Databases that applied the removed branch migrations must first return to the shared
baseline using the old migration files, or be recreated through a separately authorized
local-data reset. Applying the replacement to their current schema is unsupported.
Do not edit EF migration-history rows. New databases require SQL init → migrations →
optional synthetic seed. The destructive seed does not repair schemas.
For future rebases, verify the actual shared migration boundary, regenerate the designer
and snapshot against it, inspect for unrelated DDL, and rerun migration/backend checks.

## Repositories and dependency injection

Each repository lives in `Repositories/<Name>Repository/` with its interface;
namespace follows the folder. Services depend on IUserLimitRepository,
IGroupLimitRepository, ILimitAlertRepository and ILimitSweepRepository, registered
as scoped in AddLimits. LimitSweepCandidate has its own file beside the sweep
repository; LimitQuery is shared by user/group repositories in the root folder.
Repositories preserve tracking, locking and queries, and do not commit transactions.

## Verification

Limits.UnitTests covers calendar boundaries, validation, usage and sweep lifecycle.
Limits.IntegrationTests uses TEST_POSTGRES_CONNECTION_STRING and a separate disposable
SQL-init/migrations database per test. It exercises real Expense, Grouping and inbox
services, plus HTTP/OpenAPI with production AddLimits registrations. No PostgreSQL
configuration skips database tests locally and is an error in CI. Run both Limits test
projects and relevant backend regressions; results are recorded in implementation-status.
