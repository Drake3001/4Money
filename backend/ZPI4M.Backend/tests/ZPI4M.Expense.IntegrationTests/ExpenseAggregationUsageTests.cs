using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Grouping.Services.Transactions;

namespace ZPI4M.Expense.IntegrationTests;

public sealed partial class ExpenseAggregationTests
{
	[PostgresTheory]
	[InlineData(ExpenseAggregationScope.Personal, 85)]
	[InlineData(ExpenseAggregationScope.Group, 200)]
	public async Task BudgetTotalsAgreeWithReportsAndNullCategoryIncludesAll(ExpenseAggregationScope scope, decimal expected)
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		Guid? group = scope == ExpenseAggregationScope.Group ? Group : null;
		var total = await service.GetUsageTotalAsync(UserId, From, To, scope, group, null);
		Assert.Equal(new ExpenseUsageTotal(expected), total.Value);
		var categories = (await service.GetCategoryBreakdownAsync(UserId, From, To, scope, group, [Food, Cleaning])).Value!;
		Assert.Equal(expected, categories.Sum(c => c.TotalAmount));
		foreach (var category in categories)
			Assert.Equal(category.TotalAmount, (await service.GetUsageTotalAsync(UserId, From, To, scope, group, category.CategoryId)).Value!.Amount);
		Assert.Equal(0m, (await service.GetUsageTotalAsync(UserId, From, To, scope, group, Guid.NewGuid())).Value!.Amount);
		Assert.Equal(0m, (await service.GetUsageTotalAsync(UserId, new(2030, 1, 1), new(2030, 1, 1), scope, group, null)).Value!.Amount);
	}

	[PostgresFact]
	public async Task BudgetDatesUseUtcForLegacyTimesAndIncludeBothEdges()
	{
		await PrepareAsync();
		await ExecuteAsync("DELETE FROM expense.expense_share; DELETE FROM expense.expense_item; DELETE FROM expense.expense;");
		await using var db = OpenExpense();
		foreach (var day in new[] { new DateOnly(2026, 3, 29), new(2026, 10, 25), new(2026, 12, 31) })
			Add(db, Group, UserId, day, 100m, [(Food, 10m)]);
		await db.SaveChangesAsync();
		await ExecuteAsync("UPDATE expense.expense SET transaction_time = transaction_time + interval '23 hours 59 minutes';");
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		foreach (var day in new[] { new DateOnly(2026, 3, 29), new(2026, 10, 25), new(2026, 12, 31) })
		{
			Assert.Equal(10m, (await service.GetUsageTotalAsync(UserId, day, day, ExpenseAggregationScope.Personal, null, null)).Value!.Amount);
			Assert.Equal(10m, Assert.Single((await service.GetDailyExpenseTotalsAsync(UserId, day, day, ExpenseAggregationScope.Personal, null, [Food])).Value!).TotalAmount);
		}
		Assert.Equal(30m, (await service.GetUsageTotalAsync(UserId, DateOnly.MinValue, DateOnly.MaxValue, ExpenseAggregationScope.Personal, null, null)).Value!.Amount);
	}

	[PostgresFact]
	public async Task BudgetReadRejectsInvalidScopeAndNeverGrantsAccess()
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		Assert.Equal(ExpenseErrorCode.Forbidden, (await service.GetUsageTotalAsync(Guid.Empty, From, To, ExpenseAggregationScope.Personal, null, null)).Error);
		Assert.Equal(ExpenseErrorCode.NotFound, (await service.GetUsageTotalAsync(Guid.NewGuid(), From, To, ExpenseAggregationScope.Group, Group, null)).Error);
		Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetUsageTotalAsync(UserId, From, To, ExpenseAggregationScope.Group, null, null)).Error);
		Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetUsageTotalAsync(UserId, To, From, ExpenseAggregationScope.Personal, null, null)).Error);
		Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetUsageTotalAsync(UserId, From, To, ExpenseAggregationScope.Personal, null, Guid.Empty)).Error);
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetUsageTotalAsync(UserId, From, To, ExpenseAggregationScope.Personal, null, null, cancelled.Token));
	}

	[PostgresFact]
	public async Task SystemGroupReadRequiresCoordinatorAndSeesOnlyLockedGroupIncludingUncommittedRows()
	{
		await PrepareAsync();
		await using var grouping = OpenGrouping();
		await using var inbox = new ZPI4M.Notifications.Database.NotificationsDbContext(
			new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ZPI4M.Notifications.Database.NotificationsDbContext>()
				.UseNpgsql(ConnectionString).Options);
		var tx = new GroupingTransactionCoordinator(grouping, inbox, Microsoft.Extensions.Logging.Abstractions.NullLogger<GroupingTransactionCoordinator>.Instance);
		// Share the coordinator connection explicitly, as production DI does.
		await using var db = new ZPI4M.Expense.Database.ExpenseDbContext(
			new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ZPI4M.Expense.Database.ExpenseDbContext>()
				.UseNpgsql(grouping.Database.GetDbConnection()).Options);
		var service = new ExpenseSystemAggregationService(new ExpenseRepository(db), tx);
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetGroupUsageTotalAsync(Group, From, To, null));
		await tx.ExecuteAsync(async token =>
		{
			await tx.EnlistAsync(db, token);
			await grouping.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM grouping.\"group\" WHERE id = {Group} FOR UPDATE", token);
			Add(db, Group, UserId, From, 100m, [(Food, 5m)]);
			await db.SaveChangesAsync(token);
			Assert.Equal(205m, (await service.GetGroupUsageTotalAsync(Group, From, To, null, token)).Value!.Amount);
			Assert.Equal(165m, (await service.GetGroupUsageTotalAsync(Group, From, To, Food, token)).Value!.Amount);
			return false;
		}, success => success);
		await using var fresh = OpenExpense();
		Assert.Equal(200m, await new ExpenseRepository(fresh).GetUsageTotalAsync(UserId, [Group], From, To, ExpenseAggregationScope.Group, Group, null));
	}

	[PostgresFact]
	public async Task UpdatedSeedCanBeRepeatedWithFinalBudgetSchema()
	{
		await SeedAsync();
		await SeedAsync();
		Assert.Equal(8L, await ScalarAsync<long>("SELECT count(*) FROM limits.group_limit"));
		Assert.Equal(6L, await ScalarAsync<long>("SELECT count(*) FROM limits.user_limit"));
		Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM limits.user_limit_alert_state"));
		Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM limits.group_limit_alert_state"));
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM (SELECT period FROM limits.group_limit WHERE period=4 UNION ALL SELECT period FROM limits.user_limit WHERE period=4) custom"));
	}
}
