using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.ExpenseMetadata;

namespace ZPI4M.Expense.IntegrationTests;

public sealed class ExpenseAggregationTests : PostgresFixture
{
	private static readonly Guid Group = Guid.Parse("20000000-0000-0000-0000-000000000001");
	private static readonly Guid PrivateGroup = Guid.Parse("20000000-0000-0000-0000-000000000002");
	private static readonly Guid Other = Guid.Parse("10000000-0000-0000-0000-000000000002");
	private static readonly Guid Food = Guid.Parse("30000000-0000-0000-0000-000000000001");
	private static readonly Guid Cleaning = Guid.Parse("30000000-0000-0000-0000-000000000010");
	private static readonly DateOnly From = new(2026, 1, 1);
	private static readonly DateOnly To = new(2027, 12, 31);

	[PostgresTheory]
	[InlineData(ExpenseAggregationScope.Personal)]
	[InlineData(ExpenseAggregationScope.Group)]
	public async Task EmptySelectionReturnsEmptyForAllFourReports(ExpenseAggregationScope scope)
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		Guid? group = scope == ExpenseAggregationScope.Group ? Group : null;
		Assert.Empty((await service.GetCategoryBreakdownAsync(UserId, From, To, scope, group, [])).Value!);
		Assert.Empty((await service.GetExpenseTotalsByPeriodAndCategoryAsync(UserId, From, To, scope, group, [], ExpenseTotalsGranularity.Month)).Value!);
		Assert.Empty((await service.GetExpenseTotalsByDayOfWeekAsync(UserId, From, To, scope, group, [])).Value!);
		Assert.Empty((await service.GetDailyExpenseTotalsAsync(UserId, From, To, scope, group, [])).Value!);
		Assert.Empty(db.ChangeTracker.Entries());
	}

	[PostgresTheory]
	[InlineData(ExpenseAggregationScope.Personal, "69", "16")]
	[InlineData(ExpenseAggregationScope.Group, "160", "40")]
	public async Task CategorySelectionSumsOnlyMatchingItemsWithoutDoubleCounting(ExpenseAggregationScope scope, string food, string cleaning)
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		Guid? group = scope == ExpenseAggregationScope.Group ? Group : null;
		var all = (await service.GetCategoryBreakdownAsync(UserId, From, To, scope, group, [Food, Cleaning])).Value!;
		Assert.Equal(decimal.Parse(food), all.Single(row => row.CategoryId == Food).TotalAmount);
		Assert.Equal(decimal.Parse(cleaning), all.Single(row => row.CategoryId == Cleaning).TotalAmount);
		var selected = (await service.GetCategoryBreakdownAsync(UserId, From, To, scope, group, [Food, Food])).Value!;
		Assert.Equal(all.Single(row => row.CategoryId == Food), Assert.Single(selected));
		Assert.Empty((await service.GetCategoryBreakdownAsync(UserId, From, To, scope, group, [Guid.NewGuid()])).Value!);
		Assert.Empty(db.ChangeTracker.Entries());
	}

	[PostgresTheory]
	[InlineData(ExpenseTotalsGranularity.Month)]
	[InlineData(ExpenseTotalsGranularity.Quarter)]
	[InlineData(ExpenseTotalsGranularity.Year)]
	public async Task CalendarPeriodsIncludePartialEdgesAndSeparateYears(ExpenseTotalsGranularity granularity)
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		var rows = (await service.GetExpenseTotalsByPeriodAndCategoryAsync(UserId, new(2026, 1, 16),
			new(2027, 1, 4), ExpenseAggregationScope.Group, Group, [Food, Cleaning], granularity)).Value!;
		Assert.Equal(100m, rows.Sum(row => row.TotalAmount));
		Assert.All(rows, row => Assert.Equal(Food, row.CategoryId));
		var first = granularity switch
		{
			ExpenseTotalsGranularity.Month => new DateOnly(2026, 2, 1),
			_ => new DateOnly(2026, 1, 1)
		};
		Assert.Equal(first, rows[0].Period);
		Assert.Equal(new DateOnly(2027, 1, 1), rows[^1].Period);
		Assert.Equal(30m, rows[^1].TotalAmount);
		Assert.Equal(granularity == ExpenseTotalsGranularity.Year ? 2 : 3, rows.Count);
	}

	[PostgresFact]
	public async Task DailyAndWeekdayTotalsAreSparseAndUseTheEntireRange()
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		var daily = (await service.GetDailyExpenseTotalsAsync(UserId, From, To, ExpenseAggregationScope.Group, Group, [Food])).Value!;
		Assert.Equal(4, daily.Count);
		Assert.Equal(new DateOnly(2026, 1, 15), daily[0].Date);
		Assert.Equal(new DateOnly(2027, 1, 4), daily[^1].Date);
		Assert.Equal(160m, daily.Sum(row => row.TotalAmount));
		var weekday = (await service.GetExpenseTotalsByDayOfWeekAsync(UserId, From, To, ExpenseAggregationScope.Group, Group, [Food])).Value!;
		Assert.Equal(2, weekday.Count);
		Assert.Equal(new ExpenseDayOfWeekTotal(DayOfWeek.Monday, 100m), weekday[0]);
		Assert.Equal(new ExpenseDayOfWeekTotal(DayOfWeek.Thursday, 60m), weekday[1]);
		var personal = (await service.GetDailyExpenseTotalsAsync(UserId, From, To, ExpenseAggregationScope.Personal, null, [Food])).Value!;
		Assert.Equal(69m, personal.Sum(row => row.TotalAmount));
		Assert.Equal(3, personal.Count); // Paying for someone else is not a personal cost.
	}

	[PostgresTheory]
	[InlineData(ExpenseTotalsGranularity.Month, 4)]
	[InlineData(ExpenseTotalsGranularity.Quarter, 3)]
	[InlineData(ExpenseTotalsGranularity.Year, 2)]
	public async Task PersonalPeriodTotalsUseSharesRatherThanAmountsPaid(ExpenseTotalsGranularity granularity, int count)
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var rows = (await Service(db, grouping).GetExpenseTotalsByPeriodAndCategoryAsync(UserId, From, To,
			ExpenseAggregationScope.Personal, null, [Food, Cleaning], granularity)).Value!;
		Assert.Equal(count, rows.Count);
		Assert.Equal(69m, rows.Where(row => row.CategoryId == Food).Sum(row => row.TotalAmount));
		Assert.Equal(16m, rows.Where(row => row.CategoryId == Cleaning).Sum(row => row.TotalAmount));
	}

	[PostgresFact]
	public async Task AllSevenWeekdaysAreDistinctIncludingSunday()
	{
		await PrepareAsync();
		await using (var write = OpenExpense())
		{
			for (var day = 1; day <= 7; day++)
				Add(write, Group, UserId, new(2028, 5, day), 100m, [(Food, day)]);
			await write.SaveChangesAsync();
		}
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var rows = (await Service(db, grouping).GetExpenseTotalsByDayOfWeekAsync(UserId, new(2028, 5, 1),
			new(2028, 5, 7), ExpenseAggregationScope.Group, Group, [Food])).Value!;
		Assert.Equal(7, rows.Count);
		Assert.Equal(Enum.GetValues<DayOfWeek>().OrderBy(day => ((int)day + 6) % 7), rows.Select(row => row.DayOfWeek));
		Assert.Equal(Enumerable.Range(1, 7).Select(value => (decimal)value), rows.Select(row => row.TotalAmount));
	}

	[PostgresFact]
	public async Task SqlRoundingMatchesExistingCalculationsIncludingNegativeItemsAndCategorySelection()
	{
		await PrepareAsync();
		await using (var write = OpenExpense())
		{
			Add(write, Group, UserId, new(2029, 1, 1), 50m, [(Food, .01m), (Cleaning, .02m)]);
			Add(write, Group, Other, new(2029, 1, 1), 33.33m, [(Food, 1m), (Cleaning, -.99m)]);
			Add(write, Group, Other, new(2029, 1, 2), 50m, [(Food, 1.01m), (Cleaning, -.02m)]);
			Add(write, Group, UserId, new(2029, 1, 3), 66.67m, [(Food, 7m), (Cleaning, 3m)]);
			var item = write.ChangeTracker.Entries<ExpenseItem>().First().Entity;
			item.UnitPrice = .03m;
			item.Quantity = .5m;
			item.Discount = -.01m; // round(.015, 2) - .01 = .01
			await write.SaveChangesAsync();
		}
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		foreach (var actor in new[] { UserId, Other })
		{
			// Fixed oracle for the four synthetic purchases above: both allocations happen per expense.
			// Personal category totals: UserId (5.52, 1.67), Other (3.50, 0.34).
			var personalFood = actor == UserId ? 5.52m : 3.50m;
			var personalCleaning = actor == UserId ? 1.67m : 0.34m;
			foreach (var scope in Enum.GetValues<ExpenseAggregationScope>())
			{
				Guid? group = scope == ExpenseAggregationScope.Group ? Group : null;
				var rows = (await service.GetCategoryBreakdownAsync(actor, new(2029, 1, 1), new(2029, 1, 3), scope, group, [Food, Cleaning])).Value!;
				Assert.Equal(new[] { Food, Cleaning }, rows.Select(row => row.CategoryId));
				foreach (var row in rows)
				{
					var expected = scope == ExpenseAggregationScope.Personal
						? row.CategoryId == Food ? personalFood : personalCleaning
						: row.CategoryId == Food ? 9.02m : 2.01m;
					Assert.Equal(expected, row.TotalAmount);
					Assert.Equal(row, Assert.Single((await service.GetCategoryBreakdownAsync(actor, new(2029, 1, 1),
						new(2029, 1, 3), scope, group, [row.CategoryId])).Value!));
				}
				var daily = (await service.GetDailyExpenseTotalsAsync(actor, new(2029, 1, 1), new(2029, 1, 3), scope, group, [Food, Cleaning])).Value!;
				Assert.Equal(rows.Sum(row => row.TotalAmount), daily.Sum(row => row.TotalAmount));
			}
		}
	}

	[PostgresFact]
	public async Task ZeroNetSelectedItemsKeepTheirDateAndUnknownCategoryDoesNotCreateDates()
	{
		await PrepareAsync();
		await using (var write = OpenExpense())
		{
			Add(write, Group, UserId, new(2031, 2, 1), 100m, [(Food, 1m), (Food, -1m), (Cleaning, 1m)]);
			await write.SaveChangesAsync();
		}
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		var rows = (await service.GetDailyExpenseTotalsAsync(UserId, new(2031, 1, 1), new(2031, 12, 31),
			ExpenseAggregationScope.Group, Group, [Food])).Value!;
		Assert.Equal(new ExpenseDailyTotalsItem(new(2031, 2, 1), 0m), Assert.Single(rows));
		Assert.Empty((await service.GetDailyExpenseTotalsAsync(UserId, new(2031, 1, 1), new(2031, 12, 31),
			ExpenseAggregationScope.Group, Group, [Guid.NewGuid()])).Value!);
	}

	[PostgresFact]
	public async Task InvalidInternalCallsFailClosedAndCancellationIsPropagated()
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		Assert.Equal(ExpenseErrorCode.Forbidden,
			(await service.GetCategoryBreakdownAsync(Guid.Empty, From, To, ExpenseAggregationScope.Personal, null, [Food])).Error);
		foreach (var (scope, group) in new (ExpenseAggregationScope, Guid?)[]
		{
			(ExpenseAggregationScope.Personal, Group), (ExpenseAggregationScope.Group, null), ((ExpenseAggregationScope)99, null)
		})
			Assert.Equal(ExpenseErrorCode.InvalidExpense,
				(await service.GetDailyExpenseTotalsAsync(UserId, From, To, scope, group, [Food])).Error);
		Assert.Equal(ExpenseErrorCode.InvalidExpense,
			(await service.GetCategoryBreakdownAsync(UserId, To, From, ExpenseAggregationScope.Personal, null, [Food])).Error);
		Assert.Equal(ExpenseErrorCode.InvalidExpense,
			(await service.GetExpenseTotalsByPeriodAndCategoryAsync(UserId, From, To, ExpenseAggregationScope.Personal,
				null, [Food], (ExpenseTotalsGranularity)99)).Error);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetDailyExpenseTotalsAsync(UserId,
			From, To, ExpenseAggregationScope.Personal, null, [Food], cancellation.Token));
	}

	[PostgresFact]
	public async Task UtcBoundariesIncludeLastMicrosecondAndSupportExtremeDates()
	{
		await PrepareAsync();
		await using (var write = OpenExpense())
		{
			Add(write, Group, UserId, DateOnly.MinValue, 100m, [(Food, 1m)]);
			Add(write, Group, UserId, DateOnly.MaxValue, 100m, [(Food, 2m)]);
			Add(write, Group, UserId, new(2030, 1, 1), 100m, [(Food, 3m)]);
			foreach (var entry in write.ChangeTracker.Entries<Models.Expense>())
				entry.Entity.TransactionTime = entry.Entity.TransactionTime.AddHours(23).AddMinutes(59).AddSeconds(59).AddTicks(9999990);
			// These native .NET extremes are stored as PostgreSQL infinities by default in Npgsql.
			Add(write, Group, UserId, DateOnly.MinValue, 100m, [(Food, 4m)]);
			Add(write, Group, UserId, DateOnly.MaxValue, 100m, [(Food, 5m)]);
			write.ChangeTracker.Entries<Models.Expense>().Single(entry => entry.Entity.Amount == 5m)
				.Entity.TransactionTime = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
			await write.SaveChangesAsync();
		}
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		foreach (var (day, amount) in new[] { (DateOnly.MinValue, 5m), (DateOnly.MaxValue, 7m), (new DateOnly(2030, 1, 1), 3m) })
		{
			var row = Assert.Single((await service.GetDailyExpenseTotalsAsync(UserId, day, day, ExpenseAggregationScope.Group, Group, [Food])).Value!);
			Assert.Equal(new ExpenseDailyTotalsItem(day, amount), row);
		}
		var periods = (await service.GetExpenseTotalsByPeriodAndCategoryAsync(UserId, DateOnly.MaxValue, DateOnly.MaxValue,
			ExpenseAggregationScope.Group, Group, [Food], ExpenseTotalsGranularity.Year)).Value!;
		Assert.Equal(new ExpensePeriodCategoryTotal(new(9999, 1, 1), Food, 7m), Assert.Single(periods));
	}

	[PostgresFact]
	public async Task GroupAccessPreservesFormerMemberHistoryAndRejectsForeignGroups()
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		Assert.Equal(ExpenseErrorCode.NotFound,
			(await service.GetCategoryBreakdownAsync(UserId, From, To, ExpenseAggregationScope.Group, PrivateGroup, [Food])).Error);
		Assert.Equal(ExpenseErrorCode.NotFound,
			(await service.GetDailyExpenseTotalsAsync(UserId, From, To, ExpenseAggregationScope.Group, Guid.NewGuid(), [Food])).Error);
		await ExecuteAsync($"UPDATE grouping.group_user SET is_member = false WHERE user_id = '{UserId}' AND group_id = '{Group}'");
		var former = (await service.GetCategoryBreakdownAsync(UserId, From, To, ExpenseAggregationScope.Group, Group, [Food])).Value!;
		Assert.Equal(160m, Assert.Single(former).TotalAmount);
		// An unrelated purchase in the same group is hidden from a former member.
		await using (var write = OpenExpense())
		{
			Add(write, Group, Other, new(2026, 5, 1), 0m, [(Food, 900m)]);
			await write.SaveChangesAsync();
		}
		Assert.Equal(160m, Assert.Single((await service.GetCategoryBreakdownAsync(UserId, From, To,
			ExpenseAggregationScope.Group, Group, [Food])).Value!).TotalAmount);
	}

	private ExpenseDbContext OpenExpense() => new(new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(ConnectionString).Options);
	private GroupingDbContext OpenGrouping() => new(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
	private static ExpenseAggregationService Service(ExpenseDbContext db, GroupingDbContext grouping) =>
		new(new ExpenseRepository(db), new GroupExpenseMetadataLookup(new GroupRepository(grouping)));

	private async Task PrepareAsync()
	{
		await SeedAsync();
		await ExecuteAsync("DELETE FROM expense.expense_share; DELETE FROM expense.expense_item; DELETE FROM expense.expense;");
		await using var db = OpenExpense();
		Add(db, Group, Other, new(2026, 1, 15), 40m, [(Food, 60m), (Cleaning, 40m)]);
		Add(db, Group, UserId, new(2026, 2, 2), 50m, [(Food, 50m)]);
		Add(db, Group, UserId, new(2026, 4, 6), 100m, [(Food, 20m)]);
		Add(db, Group, UserId, new(2027, 1, 4), 0m, [(Food, 30m)]);
		Add(db, PrivateGroup, Other, new(2026, 2, 2), 0m, [(Food, 900m)]);
		await db.SaveChangesAsync();
	}

	private static void Add(ExpenseDbContext db, Guid group, Guid payer, DateOnly day, decimal percentage,
		(Guid Category, decimal Amount)[] items)
	{
		var id = Guid.NewGuid();
		db.Expenses.Add(new Models.Expense
		{
			Id = id,
			FkGroupId = group,
			FkPayerId = payer,
			Name = "Synthetic report purchase",
			TransactionTime = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
			Amount = items.Sum(item => item.Amount),
			ExpenseItems = items.Select(item => new ExpenseItem
			{
				Id = Guid.NewGuid(),
				FkExpenseId = id,
				FkCategoryId = item.Category,
				Name = "Synthetic item",
				UnitPrice = item.Amount,
				Quantity = 1m
			}).ToList()
		});
		if (percentage > 0m) db.ExpenseShares.Add(new() { FkExpenseId = id, FkUserId = UserId, Percentage = percentage });
		if (percentage < 100m) db.ExpenseShares.Add(new() { FkExpenseId = id, FkUserId = Other, Percentage = 100m - percentage });
	}
}
