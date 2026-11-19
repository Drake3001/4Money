using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Access;
using ZPI4M.Limits.DTOs.UserLimits;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Repositories.LimitSweepRepository;
using ZPI4M.Limits.Services.Alerts;
using ZPI4M.Limits.Services.Calendar;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Limits.IntegrationTests;

public sealed partial class LimitAlertTests
{
	private static CreateUserLimitRequest Personal(LimitPeriod period = LimitPeriod.Month, Guid? category = null, string amount = "100") => new()
	{
		Amount = amount,
		CurrencyCode = "PLN",
		Period = period,
		CategoryId = category,
		StartDate = period == LimitPeriod.Custom ? new(2026, 11, 1) : null,
		EndDate = period == LimitPeriod.Custom ? new(2026, 11, 30) : null
	};
	[PostgresFact]
	public async Task EveryPeriodReturnsFiniteDatesIncludingDisabledAndFinishedDefinitions()
	{
		await PrepareAsync();
		foreach (var period in Enum.GetValues<LimitPeriod>())
		{
			Guid personalId, groupId;
			var expected = period == LimitPeriod.Custom
				? new LimitWindow(new(2026, 11, 1), new(2026, 11, 30))
				: LimitCalendar.For(period, new(2026, 11, 14));
			await using (var s = Open())
			{
				var personal = (await s.Users.CreateAsync(Admin, Personal(period))).Value!;
				var group = (await s.Groups.CreateAsync(Admin, GroupId, new()
				{
					Amount = "100",
					Period = period,
					StartDate = period == LimitPeriod.Custom ? expected.Start : null,
					EndDate = period == LimitPeriod.Custom ? expected.End : null
				})).Value!;
				Assert.Equal(expected.Start, personal.PeriodStartDate);
				Assert.Equal(expected.End, personal.PeriodEndDate);
				Assert.Equal(expected.Start, group.PeriodStartDate);
				Assert.Equal(expected.End, group.PeriodEndDate);
				personalId = personal.Id;
				groupId = group.Id;
				Assert.Equal(200, (await s.Users.SetEnabledAsync(Admin, personal.Id, new() { IsEnabled = false, Version = personal.Version })).Status);
				Assert.Equal(200, (await s.Groups.SetEnabledAsync(Admin, GroupId, group.Id, new() { IsEnabled = false, Version = group.Version })).Status);
			}
			await using var read = Open(now: "2027-01-15T12:00:00Z");
			if (period != LimitPeriod.Custom) expected = LimitCalendar.For(period, new(2027, 1, 15));
			var disabledPersonal = (await read.Users.GetAsync(Admin, personalId)).Value!;
			var disabledGroup = (await read.Groups.GetAsync(Admin, GroupId, groupId)).Value!;
			Assert.False(disabledPersonal.IsEnabled);
			Assert.False(disabledGroup.IsEnabled);
			Assert.Equal(expected.Start, disabledPersonal.PeriodStartDate);
			Assert.Equal(expected.End, disabledPersonal.PeriodEndDate);
			Assert.Equal(expected.Start, disabledGroup.PeriodStartDate);
			Assert.Equal(expected.End, disabledGroup.PeriodEndDate);
		}
	}

	[PostgresFact]
	public async Task DefinitionPersistsAcrossCalendarChangesAndDisabledUsageRemainsLive()
	{
		await PrepareAsync();
		Guid id;
		string version;
		await using (var s = Open())
		{
			await s.CreateAsync(50);
			var result = await s.Users.CreateAsync(Admin, Personal());
			Assert.Equal(201, result.Status);
			id = result.Value!.Id;
			version = result.Value.Version;
			Assert.Equal(new DateOnly(2026, 11, 1), result.Value.PeriodStartDate);
			Assert.Equal("50.00", (await s.Users.GetAsync(Admin, id)).Value!.Usage!.SpentAmount);
		}

		await using (var s = Open(now: "2027-01-15T12:00:00Z"))
		{
			var result = (await s.Users.GetAsync(Admin, id)).Value!;
			Assert.Equal(new DateOnly(2027, 1, 1), result.PeriodStartDate);
			Assert.Equal("0.00", result.Usage!.SpentAmount);
			Assert.Equal(version, result.Version);
			Assert.Single(await s.Limits.UserLimits.ToArrayAsync());
			var disabled = await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = false, Version = version });
			Assert.Equal(200, disabled.Status);
			Assert.Empty((await s.Users.ListAsync(Admin, new())).Value!.Items);
			Assert.Single((await s.Users.ListAsync(Admin, new() { IncludeDisabled = true })).Value!.Items);
			Assert.NotNull((await s.Users.GetAsync(Admin, id)).Value!.Usage);
		}
	}

	[PostgresFact]
	public async Task AmountAndEnableChangesProtectVersionsAndUniquenessAcrossPeriods()
	{
		await PrepareAsync();
		Guid id;
		string version;
		await using (var s = Open())
		{
			var row = (await s.Users.CreateAsync(Admin, Personal())).Value!;
			id = row.Id;
			version = row.Version;
		}

		await using (var s = Open())
		{
			var updated = await s.Users.UpdateAsync(Admin, id, new() { Amount = "200", Version = version });
			Assert.Equal(200, updated.Status);
			Assert.NotEqual(version, updated.Value!.Version);
		}

		await using (var s = Open())
			Assert.Equal("VersionConflict", (await s.Users.UpdateAsync(Admin, id, new() { Amount = "300", Version = version })).Code);
		await using (var s = Open())
			Assert.Equal("OverlappingBudget", (await s.Users.CreateAsync(Admin, Personal(LimitPeriod.Week))).Code);
		await using (var s = Open())
		{
			var current = (await s.Users.GetAsync(Admin, id)).Value!;
			await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = false, Version = current.Version });
		}

		await using (var s = Open())
			Assert.Equal(201, (await s.Users.CreateAsync(Admin, Personal(LimitPeriod.Custom))).Status);
		await using (var s = Open())
		{
			var disabled = (await s.Users.GetAsync(Admin, id)).Value!;
			Assert.Equal(200, (await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = true, Version = disabled.Version })).Status);
		}

		await using (var s = Open())
			Assert.Equal(201, (await s.Users.CreateAsync(Admin, Personal(category: Category))).Status);
	}

	[PostgresFact]
	public async Task PermissionsCurrencyAndValidationRemainExplicit()
	{
		await PrepareAsync();
		Guid id;
		await using (var s = Open())
			id = (await s.Users.CreateAsync(Admin, Personal())).Value!.Id;
		await using (var s = Open())
			Assert.Equal(404, (await s.Users.GetAsync(Member, id)).Status);
		await using (var s = Open())
			Assert.Equal(404, (await s.Users.UpdateAsync(Member, id, new() { Amount = "bad", Version = "bad" })).Status);
		await using (var s = Open())
			Assert.Equal(400, (await s.Users.CreateAsync(Admin, new() { Amount = "100", CurrencyCode = "EUR", Period = LimitPeriod.Month })).Status);
		await using (var s = Open())
			Assert.Equal(403, (await s.Groups.CreateAsync(Member, GroupId, new() { Amount = "100", Period = LimitPeriod.Month })).Status);
		await using (var s = Open())
			Assert.Equal(404, (await s.Groups.ListAsync(Former, GroupId, new())).Status);
		await ExecuteAsync("UPDATE grouping.\"group\" SET currency_code='EUR'");
		await using (var s = Open())
			Assert.Equal(400, (await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Month })).Status);
	}

	[PostgresFact]
	public async Task ThresholdsDeduplicateAcrossCorrectionsAmountEditsAndReenable()
	{
		await PrepareAsync();
		Guid id;
		await using (var s = Open())
			id = await s.PersonalAsync(Admin);
		ZPI4M.Expense.Services.Contracts.ExpenseDetails expense;
		await using (var s = Open())
			expense = await s.CreateAsync(80);
		Assert.Single(await AlertsAsync());
		await using (var s = Open())
			expense = await s.UpdateAsync(expense, 110);
		Assert.Equal(2, (await AlertsAsync()).Length);
		await using (var s = Open())
			expense = await s.UpdateAsync(expense, 10);
		await using (var s = Open())
		{
			var current = (await s.Users.GetAsync(Admin, id)).Value!;
			var changed = (await s.Users.UpdateAsync(Admin, id, new() { Amount = "5", Version = current.Version })).Value!;
			var disabled = (await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = false, Version = changed.Version })).Value!;
			await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = true, Version = disabled.Version });
			var state = await s.Limits.UserLimitAlertStates.SingleAsync();
			Assert.NotNull(state.ExceededNotifiedAt);
		}

		Assert.Equal(2, (await AlertsAsync()).Length);
	}

	[PostgresFact]
	public async Task SweepHandlesFutureSpendingAtBoundaryAndNeverBackfillsHistoricalAlerts()
	{
		await PrepareAsync();
		Guid id;
		await using (var s = Open(now: "2026-10-31T12:00:00Z"))
		{
			id = (await s.Users.CreateAsync(Admin, Personal())).Value!.Id;
			await s.CreateAsync(110);
		}

		Assert.Empty(await AlertsAsync());
		await using var provider = SweepHost("2026-11-01T12:00:00Z");
		var service = provider.GetRequiredService<ILimitAlertSweepService>();
		Assert.Equal(1, (await service.RunAsync()).Evaluated);
		Assert.Single(await AlertsAsync());
		await service.RunAsync();
		Assert.Single(await AlertsAsync());
		await using var later = SweepHost("2027-03-01T12:00:00Z");
		await later.GetRequiredService<ILimitAlertSweepService>().RunAsync();
		await using var read = Open();
		Assert.Single(await read.Limits.UserLimits.ToArrayAsync());
		Assert.Single(await read.Limits.UserLimitAlertStates.ToArrayAsync());
	}

	[PostgresFact]
	public async Task CustomHasTripDeduplicationAndGroupRecipientsAreCurrentActiveMembers()
	{
		await PrepareAsync();
		Guid id;
		await using (var s = Open())
		{
			id = (await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Custom, StartDate = new(2026, 11, 14), EndDate = new(2026, 12, 15) })).Value!.Id;
			await s.CreateAsync(110);
		}

		var notification = Assert.Single(await AlertsAsync());
		Assert.Equal(2, notification.NotificationRecipients.Count);
		Assert.Equal(GroupId, notification.FkGroupId);
		Assert.Null(notification.FkExpenseId);
		await using var host = SweepHost("2027-01-01T12:00:00Z");
		await host.GetRequiredService<ILimitAlertSweepService>().RunAsync();
		Assert.Single(await AlertsAsync());
		await using var read = Open();
		Assert.Equal(new DateOnly(2026, 11, 14), (await read.Limits.GroupLimitAlertStates.SingleAsync()).PeriodStartDate);
	}

	[PostgresFact]
	public async Task NoRecipientsIsRetriedAndArchivedGroupsAreSkipped()
	{
		await PrepareAsync();
		await ExecuteAsync("UPDATE \"user\".\"user\" SET is_active=false");
		await using (var s = Open())
		{
			await s.PersonalAsync(Admin);
			await s.CreateAsync(110);
		}

		Assert.Empty(await AlertsAsync());
		await using (var s = Open())
			Assert.Empty(await s.Limits.UserLimitAlertStates.ToArrayAsync());
		await ExecuteAsync("UPDATE \"user\".\"user\" SET is_active=true WHERE _id='10000000-0000-0000-0000-000000000001'");
		await using var host = SweepHost();
		await host.GetRequiredService<ILimitAlertSweepService>().RunAsync();
		Assert.Single(await AlertsAsync());
		await using (var s = Open())
			await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "1", Period = LimitPeriod.Month });
		await ExecuteAsync("UPDATE grouping.\"group\" SET is_archived=true");
		var result = await host.GetRequiredService<ILimitAlertSweepService>().RunAsync();
		Assert.True(result.Skipped >= 1);
	}

	[PostgresFact]
	public async Task FailedAlertStateRollsBackExpenseBalancesAndInbox()
	{
		await PrepareAsync();
		await using (var s = Open())
			await s.PersonalAsync(Admin);
		await ExecuteAsync("""
   CREATE FUNCTION limits.reject_state() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic failure'; END $$;
   CREATE TRIGGER reject_state BEFORE INSERT ON limits.user_limit_alert_state FOR EACH ROW EXECUTE FUNCTION limits.reject_state();
   """);
		await using (var s = Open())
			await Assert.ThrowsAsync<DbUpdateException>(() => s.CreateAsync(110));
		await using var read = Open();
		Assert.Empty(await read.Expense.Expenses.ToArrayAsync());
		Assert.Empty(await read.Inbox.Notifications.ToArrayAsync());
		Assert.All(await read.Grouping.GroupMembers.ToArrayAsync(), m =>
		{
			Assert.Equal(0, m.TotalPaid);
			Assert.Equal(0, m.TotalShare);
		});
	}

	[PostgresFact]
	public async Task ConcurrentCreatesAndSweepsDoNotDuplicateDefinitionsOrAlerts()
	{
		await PrepareAsync();
		var created = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
		{
			await using var s = Open();
			return await s.Users.CreateAsync(Admin, Personal());
		}));
		Assert.Equal(new[] { 201, 409 }, created.Select(r => r.Status).Order().ToArray());
		await ExecuteAsync("UPDATE \"user\".\"user\" SET is_active=false");
		await using (var s = Open())
			await s.CreateAsync(110);
		await ExecuteAsync("UPDATE \"user\".\"user\" SET is_active=true");
		await using var a = SweepHost();
		await using var b = SweepHost();
		await Task.WhenAll(a.GetRequiredService<ILimitAlertSweepService>().RunAsync(), b.GetRequiredService<ILimitAlertSweepService>().RunAsync());
		Assert.Single(await AlertsAsync());
		await using var read = Open();
		Assert.Single(await read.Limits.UserLimitAlertStates.ToArrayAsync());
	}

	[PostgresFact]
	public async Task SweepBudgetIsBoundedAndCursorReachesOtherKindsAfterFailure()
	{
		await PrepareAsync();
		await using (var s = Open())
		{
			await s.PersonalAsync(Admin);
			await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Month });
		}

		await using var host = SweepHost(max: 1, unavailable: true);
		var service = host.GetRequiredService<ILimitAlertSweepService>();
		Assert.Equal(1, (await service.RunAsync()).Failed);
		Assert.Equal(1, (await service.RunAsync()).Failed);
		// The cursor wraps and retries instead of permanently abandoning failures.
		Assert.Equal(1, (await service.RunAsync()).Failed);
	}

	[PostgresFact]
	public async Task GroupMutationsAreVersionedAndFiltersCountBeforePaging()
	{
		await PrepareAsync();
		Guid id;
		string version;
		await using (var scope = Open())
		{
			var first = (await scope.Groups.CreateAsync(Admin, GroupId, new() { Amount = "100", Period = LimitPeriod.Month })).Value!;
			id = first.Id;
			version = first.Version;
			await scope.Groups.CreateAsync(Admin, GroupId, new() { Amount = "200", Period = LimitPeriod.Week, CategoryId = Category });
			Assert.Equal(2, (await scope.Groups.ListAsync(Member, GroupId, new() { PageSize = 1 })).Value!.TotalCount);
			Assert.Single((await scope.Groups.ListAsync(Member, GroupId, new() { Period = LimitPeriod.Week })).Value!.Items);
			Assert.Equal(404, (await scope.Groups.GetAsync(Admin, OtherGroupId, id)).Status);
		}

		await using (var scope = Open())
			Assert.Equal(403, (await scope.Groups.UpdateAsync(Member, GroupId, id, new() { Amount = "150", Version = version })).Status);
		await using (var scope = Open())
		{
			var updated = await scope.Groups.UpdateAsync(Admin, GroupId, id, new() { Amount = "150", Version = version });
			Assert.Equal(200, updated.Status);
			Assert.Equal("150.00", updated.Value!.Amount);
			Assert.NotEqual(version, updated.Value.Version);
		}

		await using (var scope = Open())
			Assert.Equal("VersionConflict", (await scope.Groups.SetEnabledAsync(Admin, GroupId, id, new() { IsEnabled = false, Version = version })).Code);
	}

	[PostgresFact]
	public async Task ConcurrentEnablesAndStaleEditsHaveOneWinner()
	{
		await PrepareAsync();
		Guid id;
		string original;
		await using (var s = Open())
		{
			var row = (await s.Users.CreateAsync(Admin, Personal())).Value!;
			id = row.Id;
			original = row.Version;
		}

		var edits = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
		{
			await using var s = Open();
			return await s.Users.UpdateAsync(Admin, id, new() { Amount = (200 + i).ToString(), Version = original });
		}));
		Assert.Equal(new[] { 200, 409 }, edits.Select(e => e.Status).Order().ToArray());
		string disabled;
		await using (var s = Open())
		{
			var current = (await s.Users.GetAsync(Admin, id)).Value!;
			disabled = (await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = false, Version = current.Version })).Value!.Version;
		}

		var race = await Task.WhenAll(Task.Run(async () =>
		{
			await using var s = Open();
			return (await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = true, Version = disabled })).Status;
		}), Task.Run(async () =>
		{
			await using var s = Open();
			return (await s.Users.CreateAsync(Admin, Personal())).Status;
		}));
		Assert.Single(race, status => status is 200 or 201);
		Assert.Single(race, status => status == 409);
	}

	[PostgresFact]
	public async Task SweepRacingExpenseWriteAndOuterRollbackPreserveAtomicity()
	{
		await PrepareAsync();
		await using (var s = Open())
			await s.PersonalAsync(Admin);
		await using var provider = SweepHost();
		await Task.WhenAll(provider.GetRequiredService<ILimitAlertSweepService>().RunAsync(), Task.Run(async () =>
		{
			await using var s = Open();
			await s.CreateAsync(110);
		}));
		Assert.Single(await AlertsAsync());
		await using (var s = Open())
		{
			var result = await s.Tx.ExecuteAsync(async ct =>
			{
				var group = await s.Groups.CreateAsync(Admin, GroupId, new() { Amount = "1", Period = LimitPeriod.Month }, ct);
				Assert.Equal(201, group.Status);
				return false;
			}, v => v, default);
			Assert.False(result);
		}

		await using var read = Open();
		Assert.Empty(await read.Limits.GroupLimits.ToArrayAsync());
		Assert.Empty(await read.Limits.GroupLimitAlertStates.ToArrayAsync());
		Assert.Single(await AlertsAsync());
	}

	[PostgresFact]
	public async Task DatabaseConstraintsProtectScopeAndCurrencyAndDisabledWritesDoNotAlert()
	{
		await PrepareAsync();
		Guid id;
		string version;
		await using (var s = Open())
		{
			var row = (await s.Users.CreateAsync(Admin, Personal())).Value!;
			id = row.Id;
			version = row.Version;
		}

		await using (var s = Open())
			await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = false, Version = version });
		await using (var s = Open())
			await s.CreateAsync(110);
		await using (var s = Open())
		{
			var row = (await s.Users.GetAsync(Admin, id)).Value!;
			await s.Users.UpdateAsync(Admin, id, new() { Amount = "1", Version = row.Version });
		}

		Assert.Empty(await AlertsAsync());
		await using (var s = Open())
		{
			var row = (await s.Users.GetAsync(Admin, id)).Value!;
			await s.Users.SetEnabledAsync(Admin, id, new() { IsEnabled = true, Version = row.Version });
		}

		Assert.Single(await AlertsAsync());
		var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"INSERT INTO limits.user_limit(id,user_id,amount,currency_code,period) SELECT gen_random_uuid(),user_id,100,'PLN',1 FROM limits.user_limit WHERE id='{id}'"));
		Assert.Equal(PostgresErrorCodes.ExclusionViolation, duplicate.SqlState);
		var currency = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync("UPDATE limits.user_limit SET currency_code='EUR'"));
		Assert.Equal(PostgresErrorCodes.CheckViolation, currency.SqlState);
	}

	private ServiceProvider SweepHost(string now = "2026-11-14T12:00:00Z", int max = 100, bool unavailable = false)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		var clock = new SweepClock(now);
		services.AddSingleton(new LimitCalendar(clock));
		services.AddSingleton(Options.Create(new LimitsAlertsSweepOptions { MaxDefinitionsPerRun = max }));
		services.AddScoped(_ => Open(now: now, unavailable: unavailable));
		services.AddScoped<ILimitSweepRepository>(p => new LimitSweepRepository(p.GetRequiredService<Scope>().Limits));
		services.AddScoped(p =>
		{
			var s = p.GetRequiredService<Scope>();
			return new LimitAlertSweepStep(s.Limits, new LimitSweepRepository(s.Limits), s.Tx, new GroupBudgetContextService(new GroupRepository(s.Grouping), s.Tx), new UserLookupService(s.UsersDb), s.Alerts);
		});
		services.AddSingleton<ILimitAlertSweepService, LimitAlertSweepService>();
		return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
	}

	private sealed class SweepClock(string now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse(now);
	}

	[PostgresFact]
	public async Task MigrationGuardsBothDirectionsAndEmptyRoundTripPreservesOtherData()
	{
		await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		var migrations = db.Database.GetMigrations().ToArray();
		Assert.Equal("20261116211631_NormalizeExpenseCategoriesAndAddDeposit", migrations[^2]);
		Assert.EndsWith("_AddLimits", migrations[^1]);
		var previous = migrations[^2];
		var migrator = db.GetService<IMigrator>();
		await migrator.MigrateAsync(previous);
		var legacySchema = await LimitsSchemaAsync(db);
		foreach (var insert in new[]
		{
			"INSERT INTO limits.group_limit(id,group_id,amount,period) VALUES(gen_random_uuid(),'20000000-0000-0000-0000-000000000001',100,2)",
			"INSERT INTO limits.category_group_limit(fk_category_id,fk_group_id,weekly_limit) VALUES('30000000-0000-0000-0000-000000000099','20000000-0000-0000-0000-000000000001',100)"
		})
		{
			await ExecuteAsync(insert);
			var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
			Assert.Contains("requires empty Limits", failure.MessageText);
			Assert.Equal(1, await db.Database.SqlQueryRaw<long>("SELECT (SELECT count(*) FROM limits.group_limit) + (SELECT count(*) FROM limits.category_group_limit) AS \"Value\"").SingleAsync());
			Assert.Equal(legacySchema, await LimitsSchemaAsync(db));
			await ExecuteAsync("DELETE FROM limits.group_limit; DELETE FROM limits.category_group_limit");
		}
		await migrator.MigrateAsync();
		Assert.False(db.Database.HasPendingModelChanges());
		var finalSchema = await LimitsSchemaAsync(db);
		foreach (var table in new[] { "user_limit", "group_limit", "user_limit_alert_state", "group_limit_alert_state" })
		{
			// Orphan state fixtures isolate the state-table guard from the definition guards.
			var insert = table.EndsWith("alert_state", StringComparison.Ordinal)
				? $"SET session_replication_role = replica; INSERT INTO limits.{table}(limit_id,period_start_date) VALUES(gen_random_uuid(),DATE '2026-11-01'); SET session_replication_role = origin"
				: $"INSERT INTO limits.{table}(id,{(table == "user_limit" ? "user_id" : "group_id")},amount,currency_code,period) VALUES(gen_random_uuid(),'{(table == "user_limit" ? Admin : GroupId)}',100,'PLN',2)";
			await ExecuteAsync(insert);
			var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
			Assert.Contains("requires empty Limits", failure.MessageText);
			var countSql = "SELECT count(*) AS \"Value\" FROM limits." + table; // table is from the fixed list above.
			Assert.Equal(1, await db.Database.SqlQueryRaw<long>(countSql).SingleAsync());
			Assert.Equal(finalSchema, await LimitsSchemaAsync(db));
			await ExecuteAsync($"DELETE FROM limits.{table}");
		}
		await migrator.MigrateAsync(previous);
		Assert.Equal(legacySchema, await LimitsSchemaAsync(db));
		Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_extension WHERE extname='btree_gist'").SingleAsync());
		await migrator.MigrateAsync();
		Assert.Equal(finalSchema, await LimitsSchemaAsync(db));
		Assert.Equal(3, await db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM \"user\".\"user\"").SingleAsync());
		Assert.Equal(9, await db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM grouping.group_user").SingleAsync());
	}

	private static async Task<string[]> LimitsSchemaAsync(AppDbContext db) => await db.Database.SqlQueryRaw<string>("""
		SELECT 'constraint:' || c.relname || ':' || conname || ':' || pg_get_constraintdef(p.oid) AS "Value"
		FROM pg_constraint p JOIN pg_class c ON c.oid=p.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace
		WHERE n.nspname='limits'
		UNION ALL SELECT 'index:' || indexname || ':' || indexdef FROM pg_indexes WHERE schemaname='limits'
		UNION ALL SELECT 'column:' || table_name || ':' || column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(column_default,'')
		FROM information_schema.columns WHERE table_schema='limits'
		ORDER BY "Value"
		""").ToArrayAsync();
}
