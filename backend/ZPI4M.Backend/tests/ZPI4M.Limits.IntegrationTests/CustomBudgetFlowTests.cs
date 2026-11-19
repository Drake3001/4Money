using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using ZPI4M.Limits.DTOs.UserLimits;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Services.Alerts;

namespace ZPI4M.Limits.IntegrationTests;

public sealed partial class LimitAlertTests
{
	private static CreateUserLimitRequest Trip(string start = "2026-03-25", string end = "2026-04-10", Guid? category = null, string amount = "100") => new()
	{
		Amount = amount,
		CurrencyCode = "PLN",
		Period = LimitPeriod.Custom,
		StartDate = DateOnly.Parse(start),
		EndDate = DateOnly.Parse(end),
		CategoryId = category
	};
	[PostgresFact]
	public async Task CustomTripSpansMonthsWithInclusiveBoundsAndLiveFinishedUsage()
	{
		await PrepareAsync();
		Guid id;
		string version;
		ZPI4M.Expense.Services.Contracts.ExpenseDetails changed;
		await using (var s = Open(now: "2026-03-25T12:00:00Z"))
		{
			var created = await s.Users.CreateAsync(Admin, Trip(amount: "1000"));
			Assert.Equal(201, created.Status);
			id = created.Value!.Id;
			version = created.Value.Version;
			await s.CreateAsync(10, day: new(2026, 3, 24));
			changed = await s.CreateAsync(20, day: new(2026, 3, 25));
			await s.CreateAsync(30, day: new(2026, 4, 10));
			await s.CreateAsync(40, day: new(2026, 4, 11));
		}

		await using (var s = Open(now: "2026-04-11T12:00:00Z"))
		{
			var result = (await s.Users.GetAsync(Admin, id)).Value!;
			Assert.Equal(new DateOnly(2026, 3, 25), result.PeriodStartDate);
			Assert.Equal(new DateOnly(2026, 4, 10), result.PeriodEndDate);
			Assert.Equal("50.00", result.Usage!.SpentAmount);
			Assert.Equal("FinishedBudgetImmutable", (await s.Users.UpdateAsync(Admin, id, new() { Amount = "2000", Version = version })).Code);
		}

		await using (var correction = Open(now: "2026-04-11T12:00:00Z"))
			await correction.UpdateAsync(changed, 25, day: new(2026, 3, 25));
		await using (var s = Open(now: "2026-04-12T12:00:00Z"))
		{
			Assert.Equal("55.00", (await s.Users.GetAsync(Admin, id)).Value!.Usage!.SpentAmount);
			Assert.Equal(200, (await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = false, Version = version })).Status);
		}

		Assert.Empty(await AlertsAsync());
	}

	[PostgresFact]
	public async Task CustomAllowsSharedDaysAndRecurringDefinitionsInTheSameScope()
	{
		await PrepareAsync();
		await using (var s = Open(now: "2026-03-01T12:00:00Z"))
			Assert.Equal(201, (await s.Users.CreateAsync(Admin, Trip())).Status);
		await using (var s = Open(now: "2026-03-01T12:00:00Z"))
			Assert.Equal(201, (await s.Users.CreateAsync(Admin, Trip("2026-04-11", "2026-04-25"))).Status);
		await using (var s = Open(now: "2026-03-01T12:00:00Z"))
			Assert.Equal(201, (await s.Users.CreateAsync(Admin, Trip("2026-04-10", "2026-04-11"))).Status);
		await using (var s = Open(now: "2026-03-01T12:00:00Z"))
			Assert.Equal(201, (await s.Users.CreateAsync(Admin, Personal())).Status);
		await using (var s = Open(now: "2026-03-01T12:00:00Z"))
			Assert.Equal(201, (await s.Users.CreateAsync(Admin, Trip(category: Category))).Status);
		await ExecuteAsync("""
   INSERT INTO limits.user_limit(id,user_id,category_id,amount,currency_code,period,start_date,end_date)
   VALUES(gen_random_uuid(),'10000000-0000-0000-0000-000000000001',NULL,100,'PLN',4,'2026-04-10','2026-04-10');
   """);
	}

	[PostgresFact]
	public async Task CreatingMonthlyAfterTripPreservesCustomAndIncludesAprilTripExpenses()
	{
		await PrepareAsync();
		Guid id, categoryId;
		string version;
		await using (var s = Open(now: "2026-03-25T12:00:00Z"))
		{
			var created = (await s.Users.CreateAsync(Admin, Trip())).Value!;
			id = created.Id;
			version = created.Version;
			categoryId = (await s.Users.CreateAsync(Admin, Trip(category: Category, amount: "1000"))).Value!.Id;
			await s.CreateAsync(110, day: new(2026, 4, 1));
		}

		await using (var s = Open(now: "2026-04-01T12:00:00Z"))
		{
			var result = await s.Users.UpdateAsync(Admin, id, new() { Amount = "100", Version = version });
			version = result.Value!.Version;
		}

		Assert.Single(await AlertsAsync());
		await using (var s = Open(now: "2026-04-11T12:00:00Z"))
		{
			var monthly = await s.Users.CreateAsync(Admin, Personal());
			Assert.Equal(201, monthly.Status);
			var old = (await s.Users.GetAsync(Admin, id)).Value!;
			Assert.True(old.IsEnabled);
			Assert.Equal(version, old.Version);
			Assert.Equal("100.00", old.Amount);
			Assert.Equal("110.00", old.Usage!.SpentAmount);
			Assert.True((await s.Users.GetAsync(Admin, categoryId)).Value!.IsEnabled);
			var current = (await s.Users.GetAsync(Admin, monthly.Value!.Id)).Value!;
			Assert.Equal(new DateOnly(2026, 4, 1), current.PeriodStartDate);
			Assert.Equal("110.00", current.Usage!.SpentAmount);
			Assert.Equal(2, await s.Limits.UserLimitAlertStates.CountAsync());
			Assert.NotNull((await s.Limits.UserLimitAlertStates.SingleAsync(v => v.LimitId == id)).ExceededNotifiedAt);
		}

		Assert.Equal(2, (await AlertsAsync()).Length);
	}

	[PostgresFact]
	public async Task EnablingGroupCalendarPreservesFinishedTripAndItsHistory()
	{
		await PrepareAsync();
		Guid recurring, trip;
		string recurringVersion, tripVersion;
		await using (var s = Open(now: "2026-03-25T12:00:00Z"))
		{
			var monthly = (await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Month })).Value!;
			recurring = monthly.Id;
			recurringVersion = (await s.Groups.SetEnabledAsync(Admin, GroupId, recurring, new() { IsEnabled = false, Version = monthly.Version })).Value!.Version;
			var custom = (await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Custom, StartDate = new(2026, 3, 25), EndDate = new(2026, 4, 10) })).Value!;
			trip = custom.Id;
			tripVersion = custom.Version;
			await s.CreateAsync(110, day: new(2026, 4, 1));
		}

		await using (var s = Open(now: "2026-04-01T12:00:00Z"))
			tripVersion = (await s.Groups.UpdateAsync(Admin, GroupId, trip, new() { Amount = "100", Version = tripVersion })).Value!.Version;
		await using (var s = Open(now: "2026-04-11T12:00:00Z"))
		{
			Assert.Equal(200, (await s.Groups.SetEnabledAsync(Admin, GroupId, recurring, new() { IsEnabled = true, Version = recurringVersion })).Status);
			var old = (await s.Groups.GetAsync(Admin, GroupId, trip)).Value!;
			Assert.True(old.IsEnabled);
			Assert.Equal(tripVersion, old.Version);
			Assert.Equal("FinishedBudgetImmutable", (await s.Groups.UpdateAsync(Admin, GroupId, trip, new() { Amount = "200", Version = old.Version })).Code);
			Assert.Equal(2, await s.Limits.GroupLimitAlertStates.CountAsync());
		}
	}

	[PostgresFact]
	public async Task CalendarCanCoexistWithFutureAndFinishedCustomWithoutChangingThem()
	{
		await PrepareAsync();
		Guid ended;
		string version;
		await using (var s = Open(now: "2026-03-01T12:00:00Z"))
		{
			var first = (await s.Users.CreateAsync(Admin, Trip("2026-03-01", "2026-03-10"))).Value!;
			ended = first.Id;
			version = first.Version;
			Assert.Equal(201, (await s.Users.CreateAsync(Admin, Trip("2026-04-25", "2026-05-10"))).Status);
		}

		await using (var s = Open(now: "2026-04-11T12:00:00Z"))
			Assert.Equal(201, (await s.Users.CreateAsync(Admin, Personal())).Status);
		await using (var s = Open(now: "2026-04-25T12:00:00Z"))
			Assert.Equal("OverlappingBudget", (await s.Users.CreateAsync(Admin, Personal())).Code);
		await using var read = Open(now: "2026-04-11T12:00:00Z");
		var old = (await read.Users.GetAsync(Admin, ended)).Value!;
		Assert.True(old.IsEnabled);
		Assert.Equal(version, old.Version);
	}

	[PostgresFact]
	public async Task FailedCalendarCreationLeavesCustomUnchangedForUsageAndDatabaseFailures()
	{
		await PrepareAsync();
		Guid id;
		string version;
		await using (var s = Open(now: "2026-03-25T12:00:00Z"))
		{
			var created = (await s.Users.CreateAsync(Admin, Trip())).Value!;
			id = created.Id;
			version = created.Version;
		}

		await using (var s = Open(now: "2026-04-11T12:00:00Z", unavailable: true))
			await Assert.ThrowsAsync<InvalidOperationException>(() => s.Users.CreateAsync(Admin, Personal()));
		await ExecuteAsync("""
   CREATE FUNCTION limits.reject_calendar() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.period<>4 THEN RAISE EXCEPTION 'Synthetic'; END IF; RETURN NEW; END $$;
   CREATE TRIGGER reject_calendar BEFORE INSERT ON limits.user_limit FOR EACH ROW EXECUTE FUNCTION limits.reject_calendar();
   """);
		await using (var s = Open(now: "2026-04-11T12:00:00Z"))
			await Assert.ThrowsAsync<DbUpdateException>(() => s.Users.CreateAsync(Admin, Personal()));
		await using var read = Open(now: "2026-04-11T12:00:00Z");
		var old = (await read.Users.GetAsync(Admin, id)).Value!;
		Assert.True(old.IsEnabled);
		Assert.Equal(version, old.Version);
		Assert.Single(await read.Limits.UserLimits.ToArrayAsync());
		Assert.Empty(await AlertsAsync());
	}

	[PostgresFact]
	public async Task FutureTripActivationAndFinishedCorrectionsDoNotBackfillAlerts()
	{
		await PrepareAsync();
		Guid id;
		await using (var s = Open(now: "2026-03-01T12:00:00Z"))
		{
			id = (await s.Users.CreateAsync(Admin, Trip())).Value!.Id;
			await s.CreateAsync(110, day: new(2026, 3, 25));
		}

		Assert.Empty(await AlertsAsync());
		await using (var before = SweepHost("2026-03-24T12:00:00Z"))
			Assert.Equal(0, (await before.GetRequiredService<ILimitAlertSweepService>().RunAsync()).Evaluated);
		await using (var host = SweepHost("2026-03-24T23:00:00Z"))
			Assert.Equal(1, (await host.GetRequiredService<ILimitAlertSweepService>().RunAsync()).Notified);
		// Warsaw is already March 25 at this UTC instant.
		Assert.Single(await AlertsAsync());
		await using (var host = SweepHost("2026-04-11T12:00:00Z"))
			Assert.Equal(0, (await host.GetRequiredService<ILimitAlertSweepService>().RunAsync()).Evaluated);
		await using (var s = Open(now: "2026-04-11T12:00:00Z"))
		{
			var row = (await s.Users.GetAsync(Admin, id)).Value!;
			var disabled = (await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = false, Version = row.Version })).Value!;
			Assert.Equal(200, (await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = true, Version = disabled.Version })).Status);
		}

		Assert.Single(await AlertsAsync());
	}

	[PostgresFact]
	public async Task FinishedExpenseCorrectionCanCrossThresholdWithoutGeneratingAlert()
	{
		await PrepareAsync();
		Guid id;
		ZPI4M.Expense.Services.Contracts.ExpenseDetails expense;
		await using (var s = Open(now: "2026-03-25T12:00:00Z"))
		{
			id = (await s.Users.CreateAsync(Admin, Trip())).Value!.Id;
			expense = await s.CreateAsync(50, day: new(2026, 4, 1));
		}

		await using (var s = Open(now: "2026-04-11T12:00:00Z"))
			await s.UpdateAsync(expense, 110, day: new(2026, 4, 1));
		Assert.Empty(await AlertsAsync());
		await using var read = Open(now: "2026-04-11T12:00:00Z");
		Assert.Equal("110.00", (await read.Users.GetAsync(Admin, id)).Value!.Usage!.SpentAmount);
	}

	[PostgresFact]
	public async Task FailedGroupEnableRollsBackTripAndCalendarFlagsAndVersions()
	{
		await PrepareAsync();
		Guid monthly, trip;
		string version, tripVersion;
		await using (var s = Open(now: "2026-03-25T12:00:00Z"))
		{
			var initial = (await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Month })).Value!;
			monthly = initial.Id;
			version = (await s.Groups.SetEnabledAsync(Admin, GroupId, monthly, new() { IsEnabled = false, Version = initial.Version })).Value!.Version;
			var old = (await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Custom, StartDate = new(2026, 3, 25), EndDate = new(2026, 4, 10) })).Value!;
			trip = old.Id;
			tripVersion = old.Version;
		}

		await using (var s = Open(now: "2026-04-11T12:00:00Z", unavailable: true))
			await Assert.ThrowsAsync<InvalidOperationException>(() => s.Groups.SetEnabledAsync(Admin, GroupId, monthly, new() { IsEnabled = true, Version = version }));
		await using var read = Open(now: "2026-04-11T12:00:00Z");
		var oldTrip = (await read.Groups.GetAsync(Admin, GroupId, trip)).Value!;
		Assert.True(oldTrip.IsEnabled);
		Assert.Equal(tripVersion, oldTrip.Version);
		var calendar = (await read.Groups.GetAsync(Admin, GroupId, monthly)).Value!;
		Assert.False(calendar.IsEnabled);
		Assert.Equal(version, calendar.Version);
	}

	[PostgresFact]
	public async Task ConcurrentCalendarCreatesAllowOneCalendarAlongsideUnchangedCustom()
	{
		await PrepareAsync();
		Guid id;
		await using (var s = Open(now: "2026-03-25T12:00:00Z"))
			id = (await s.Users.CreateAsync(Admin, Trip())).Value!.Id;
		var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
		{
			await using var s = Open(now: "2026-04-11T12:00:00Z");
			return await s.Users.CreateAsync(Admin, Personal());
		}));
		Assert.Equal(new[] { 201, 409 }, results.Select(v => v.Status).Order().ToArray());
		await using var read = Open(now: "2026-04-11T12:00:00Z");
		Assert.Equal(2, await read.Limits.UserLimits.CountAsync());
		Assert.True((await read.Users.GetAsync(Admin, id)).Value!.IsEnabled);
	}

	[PostgresFact]
	public async Task DatabaseRejectsUnboundedOrMismatchedDateDefinitions()
	{
		await PrepareAsync();
		await using (var s = Open(now: "2026-03-25T12:00:00Z"))
			await s.Users.CreateAsync(Admin, Trip());
		foreach (var mutation in new[]
		{
			"period=99",
			"start_date=NULL",
			"end_date=start_date-1",
			"start_date='-infinity'::date",
			"end_date='infinity'::date",
			"period=2"
		}

		)
		{
			var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("UPDATE limits.user_limit SET " + mutation));
			Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
		}
	}
}
