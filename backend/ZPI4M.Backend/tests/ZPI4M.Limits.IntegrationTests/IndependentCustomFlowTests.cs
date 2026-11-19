using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Services.Alerts;

namespace ZPI4M.Limits.IntegrationTests;

public sealed partial class LimitAlertTests
{
	[PostgresFact]
	public async Task SharedExpenseCountsForEveryOverlappingLimitAndAlertsAreIndependent()
	{
		await PrepareAsync();
		await using var s = Open();
		var users = new[]
		{
			(await s.Users.CreateAsync(Admin, Personal())).Value!,
			(await s.Users.CreateAsync(Admin, Trip("2026-11-13", "2026-11-15"))).Value!,
			(await s.Users.CreateAsync(Admin, Trip("2026-11-13", "2026-11-15"))).Value!
		};
		var groups = new[]
		{
			(await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Month })).Value!,
			(await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Custom, StartDate = new(2026, 11, 13), EndDate = new(2026, 11, 15) })).Value!,
			(await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Custom, StartDate = new(2026, 11, 13), EndDate = new(2026, 11, 15) })).Value!
		};
		var expense = await s.CreateAsync(110);
		Assert.Equal(6, (await AlertsAsync()).Length);
		Assert.Equal(3, await s.Limits.UserLimitAlertStates.CountAsync());
		Assert.Equal(3, await s.Limits.GroupLimitAlertStates.CountAsync());
		foreach (var original in users)
		{
			var current = (await s.Users.GetAsync(Admin, original.Id)).Value!;
			Assert.Equal("110.00", current.Usage!.SpentAmount);
			Assert.True(current.IsEnabled);
			Assert.Equal(original.Version, current.Version);
		}
		foreach (var original in groups)
		{
			var current = (await s.Groups.GetAsync(Admin, GroupId, original.Id)).Value!;
			Assert.Equal("110.00", current.Usage!.SpentAmount);
			Assert.True(current.IsEnabled);
			Assert.Equal(original.Version, current.Version);
		}
		await using (var change = Open())
			expense = await change.UpdateAsync(expense, 120);
		await using (var host = SweepHost())
			Assert.Equal(0, (await host.GetRequiredService<ILimitAlertSweepService>().RunAsync()).Notified);
		Assert.Equal(6, (await AlertsAsync()).Length);
		await using (var deletion = Open())
			Assert.True((await deletion.Expenses.DeleteAsync(Admin, expense.Id, expense.Version.ToString("D"))).IsSuccess);
		foreach (var limit in users)
			Assert.Equal("0.00", (await s.Users.GetAsync(Admin, limit.Id)).Value!.Usage!.SpentAmount);
		foreach (var limit in groups)
			Assert.Equal("0.00", (await s.Groups.GetAsync(Admin, GroupId, limit.Id)).Value!.Usage!.SpentAmount);
		Assert.Equal(6, (await AlertsAsync()).Length);
	}

	[PostgresFact]
	public async Task ReenablingOverlappingCustomPreservesOtherDefinitionsAndVersions()
	{
		await PrepareAsync();
		await using var s = Open();
		var trip = (await s.Users.CreateAsync(Admin, Trip("2026-11-13", "2026-11-15"))).Value!;
		var other = (await s.Users.CreateAsync(Admin, Trip("2026-11-13", "2026-11-15"))).Value!;
		var monthly = (await s.Users.CreateAsync(Admin, Personal())).Value!;
		var disabled = (await s.Users.SetEnabledAsync(Admin, trip.Id, new() { IsEnabled = false, Version = trip.Version })).Value!;
		Assert.Equal(200, (await s.Users.SetEnabledAsync(Admin, trip.Id, new() { IsEnabled = true, Version = disabled.Version })).Status);
		foreach (var original in new[] { monthly, other })
		{
			var current = (await s.Users.GetAsync(Admin, original.Id)).Value!;
			Assert.True(current.IsEnabled);
			Assert.Equal(original.Version, current.Version);
		}
		var groupTrip = (await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Custom, StartDate = new(2026, 11, 13), EndDate = new(2026, 11, 15) })).Value!;
		var groupOther = (await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Custom, StartDate = new(2026, 11, 13), EndDate = new(2026, 11, 15) })).Value!;
		var groupMonthly = (await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Month })).Value!;
		var groupDisabled = (await s.Groups.SetEnabledAsync(Admin, GroupId, groupTrip.Id, new() { IsEnabled = false, Version = groupTrip.Version })).Value!;
		Assert.Equal(200, (await s.Groups.SetEnabledAsync(Admin, GroupId, groupTrip.Id, new() { IsEnabled = true, Version = groupDisabled.Version })).Status);
		foreach (var original in new[] { groupMonthly, groupOther })
		{
			var current = (await s.Groups.GetAsync(Admin, GroupId, original.Id)).Value!;
			Assert.True(current.IsEnabled);
			Assert.Equal(original.Version, current.Version);
		}
	}

	[PostgresFact]
	public async Task ConcurrentCalendarAndIdenticalCustomCreatesAllSucceed()
	{
		await PrepareAsync();
		var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(async n =>
		{
			await using var s = Open();
			return await s.Users.CreateAsync(Admin, n == 0 ? Personal() : Trip("2026-11-13", "2026-11-15"));
		}));
		Assert.All(results, result => Assert.Equal(201, result.Status));
		var groupResults = await Task.WhenAll(Enumerable.Range(0, 3).Select(async n =>
		{
			await using var s = Open();
			return await s.Groups.CreateAsync(Admin, GroupId, new()
			{
				Amount = "100",
				Period = n == 0 ? LimitPeriod.Month : LimitPeriod.Custom,
				StartDate = n == 0 ? null : new DateOnly(2026, 11, 13),
				EndDate = n == 0 ? null : new DateOnly(2026, 11, 15)
			});
		}));
		Assert.All(groupResults, result => Assert.Equal(201, result.Status));
		await using var read = Open();
		Assert.Equal(3, await read.Limits.UserLimits.CountAsync());
		Assert.Equal(3, await read.Limits.GroupLimits.CountAsync());
	}

	[PostgresFact]
	public async Task LaterOverlappingAlertFailureRollsBackEarlierAlertsAndTheExpense()
	{
		await PrepareAsync();
		await using (var s = Open())
		{
			await s.Users.CreateAsync(Admin, Personal());
			await s.Users.CreateAsync(Admin, Trip("2026-11-13", "2026-11-15"));
			await s.Users.CreateAsync(Admin, Trip("2026-11-13", "2026-11-15"));
		}
		await ExecuteAsync("""
			CREATE FUNCTION limits.reject_second_alert() RETURNS trigger LANGUAGE plpgsql AS $$
			BEGIN IF EXISTS (SELECT 1 FROM limits.user_limit_alert_state) THEN RAISE EXCEPTION 'Synthetic second alert failure'; END IF; RETURN NEW; END $$;
			CREATE TRIGGER reject_second_alert BEFORE INSERT ON limits.user_limit_alert_state FOR EACH ROW EXECUTE FUNCTION limits.reject_second_alert();
			""");
		await using (var s = Open())
			await Assert.ThrowsAsync<DbUpdateException>(() => s.CreateAsync(110));
		await using var read = Open();
		Assert.Empty(await read.Expense.Expenses.ToArrayAsync());
		Assert.Empty(await read.Limits.UserLimitAlertStates.ToArrayAsync());
		Assert.Empty(await read.Inbox.Notifications.ToArrayAsync());
		Assert.All(await read.Grouping.GroupMembers.ToArrayAsync(), member =>
		{
			Assert.Equal(0m, member.TotalPaid);
			Assert.Equal(0m, member.TotalShare);
		});
	}

	[PostgresFact]
	public async Task DatabaseAllowsCustomOverlapsButRejectsDuplicateCalendarsForBothOwnersAndCategoryScopes()
	{
		await PrepareAsync();
		foreach (var (table, ownerColumn, owner) in new[] { ("user_limit", "user_id", Admin), ("group_limit", "group_id", GroupId) })
			foreach (var category in new[] { "NULL", "'" + Category + "'::uuid" })
			{
				// Table/column names come only from this fixed list; IDs are synthetic fixture constants.
				await ExecuteAsync($"""
					INSERT INTO limits.{table}(id,{ownerColumn},category_id,amount,currency_code,period,start_date,end_date) VALUES
					(gen_random_uuid(),'{owner}',{category},100,'PLN',2,NULL,NULL),
					(gen_random_uuid(),'{owner}',{category},100,'PLN',4,'2026-11-13','2026-11-15'),
					(gen_random_uuid(),'{owner}',{category},100,'PLN',4,'2026-11-13','2026-11-15');
					""");
				var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
					INSERT INTO limits.{table}(id,{ownerColumn},category_id,amount,currency_code,period)
					VALUES(gen_random_uuid(),'{owner}',{category},100,'PLN',1);
					"""));
				Assert.Equal(PostgresErrorCodes.ExclusionViolation, error.SqlState);
			}
	}
}
