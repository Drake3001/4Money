using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Reports;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Expense.Services.Reads;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.ExpenseMetadata;

namespace ZPI4M.Expense.IntegrationTests;

public sealed class ExpenseReadTests : PostgresFixture
{
	private static readonly Guid Group = Guid.Parse("20000000-0000-0000-0000-000000000001");
	private static readonly Guid PrivateGroup = Guid.Parse("20000000-0000-0000-0000-000000000002");
	private static readonly Guid Other = Guid.Parse("10000000-0000-0000-0000-000000000002");
	private static readonly Guid Third = Guid.Parse("10000000-0000-0000-0000-000000000003");
	private static readonly Guid Food = Guid.Parse("30000000-0000-0000-0000-000000000001");
	private static readonly Guid Cleaning = Guid.Parse("30000000-0000-0000-0000-000000000010");
	private static readonly Guid First = Guid.Parse("71000000-0000-0000-0000-000000000001");
	private static readonly Guid Second = Guid.Parse("71000000-0000-0000-0000-000000000002");
	private static readonly Guid Last = Guid.Parse("71000000-0000-0000-0000-000000000003");

	[PostgresFact]
	public async Task InclusiveDateRangeIncludesTheWholeLastDayAndAllowsOneDayAndMaxDate()
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		var november = await service.GetTotalsAsync(UserId, new() { DateFrom = new(2026, 11, 1), DateTo = new(2026, 11, 30) });
		Assert.Equal(new ExpenseReportTotals(2, 150m, 65m, 50m), november.Value);
		var day = await service.GetExpensesAsync(UserId, new() { Filter = new() { DateFrom = new(2026, 11, 30), DateTo = new(2026, 11, 30) } });
		Assert.Equal(Second, Assert.Single(day.Value!.Items).Id);
		Assert.Equal(170m, (await service.GetTotalsAsync(UserId, new() { DateTo = DateOnly.MaxValue })).Value!.Amount);
		Assert.Empty(db.ChangeTracker.Entries());
	}

	[PostgresFact]
	public async Task CategoryAndPersonalTotalsAgreeAndCategoryFilterCountsOnlyMatchingLines()
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		var filter = new ExpenseReportFilter { GroupId = Group, DateTo = new(2026, 11, 30) };
		var totals = (await service.GetTotalsAsync(UserId, filter)).Value!;
		var categories = (await service.GetCategoryTotalsAsync(UserId, filter)).Value!;
		Assert.Equal(150m, categories.Sum(category => category.Totals.Amount));
		Assert.Equal(totals.UserShareAmount, categories.Sum(category => category.Totals.UserShareAmount));
		Assert.Equal(new ExpenseReportTotals(2, 110m, 49m, 50m), categories.Single(category => category.CategoryId == Food).Totals);
		Assert.Equal(new ExpenseReportTotals(1, 40m, 16m, 0m), categories.Single(category => category.CategoryId == Cleaning).Totals);
		var food = new ExpenseReportFilter { CategoryId = Food, DateTo = new(2026, 11, 30) };
		Assert.Equal(new ExpenseReportTotals(2, 110m, 49m, 50m), (await service.GetTotalsAsync(UserId, food)).Value);
		Assert.Equal(Food, Assert.Single((await service.GetCategoryTotalsAsync(UserId, food)).Value!).CategoryId);
		var page = (await service.GetExpensesAsync(UserId, new() { Filter = food })).Value!;
		Assert.Equal(150m, page.Items.Sum(entry => entry.Amount));
		Assert.Equal(2, page.TotalCount);
		var other = (await service.GetTotalsAsync(UserId, new() { GroupId = Group, UserId = Other, DateTo = new(2026, 11, 30) })).Value!;
		Assert.Equal(new ExpenseReportTotals(2, 150m, 85m, 100m), other);
	}

	[PostgresFact]
	public async Task TimelineSupportsDaysMonthsYearsAndCategorySeriesWithoutAddingEmptyPeriods()
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		foreach (var period in Enum.GetValues<ExpenseReportPeriod>())
		{
			var result = (await service.GetTimelineAsync(UserId, new() { Period = period })).Value!;
			Assert.Equal(period == ExpenseReportPeriod.Year ? 1 : period == ExpenseReportPeriod.Month ? 2 : 3, result.Count);
			Assert.Equal(170m, result.Sum(row => row.Totals.Amount));
			Assert.Equal(85m, result.Sum(row => row.Totals.UserShareAmount));
			Assert.Equal(result.OrderBy(row => row.PeriodStart), result);
		}
		var series = (await service.GetTimelineAsync(UserId, new() { ByCategory = true })).Value!;
		Assert.Equal(3, series.Count);
		Assert.Equal(49m, series.Single(row => row.PeriodStart == new DateOnly(2026, 11, 1) && row.CategoryId == Food).Totals.UserShareAmount);
		var food = (await service.GetTimelineAsync(UserId, new() { Filter = new() { CategoryId = Food } })).Value!;
		Assert.Equal(130m, food.Sum(row => row.Totals.Amount));
	}

	[PostgresFact]
	public async Task PaginationSortingAndCombinedFiltersKeepTotalsIndependentOfThePage()
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		foreach (var field in Enum.GetValues<ExpenseReportSortBy>())
		{
			var ascending = (await service.GetExpensesAsync(UserId, new() { SortBy = field, Descending = false })).Value!;
			var descending = (await service.GetExpensesAsync(UserId, new() { SortBy = field, Descending = true })).Value!;
			Assert.Equal(3, ascending.TotalCount);
			var expected = field switch
			{
				ExpenseReportSortBy.Amount => new[] { Last, Second, First },
				ExpenseReportSortBy.Merchant => new[] { First, Second, Last },
				_ => new[] { First, Second, Last }
			};
			Assert.Equal(expected, ascending.Items.Select(row => row.Id));
			Assert.Equal(expected.Reverse(), descending.Items.Select(row => row.Id));
		}
		var page = (await service.GetExpensesAsync(UserId, new() { PageSize = 1, PageNumber = 2 })).Value!;
		Assert.Equal(3, page.TotalCount);
		Assert.Equal(Second, Assert.Single(page.Items).Id);
		Assert.Equal(170m, (await service.GetTotalsAsync(UserId, new())).Value!.Amount);
		var filters = new[]
		{
			new ExpenseReportFilter { Search = "%_!" },
			new ExpenseReportFilter { PayerId = Other, ParticipantId = UserId, GroupId = Group, MinAmount = 100m, MaxAmount = 100m },
			new ExpenseReportFilter { HasBankTransaction = true, HasReceipt = true }
		};
		foreach (var filter in filters)
			Assert.Equal(First, Assert.Single((await service.GetExpensesAsync(UserId, new() { Filter = filter })).Value!.Items).Id);
		Assert.Equal(new[] { Last, Second }, (await service.GetExpensesAsync(UserId, new() { Filter = new() { HasReceipt = false } })).Value!.Items.Select(row => row.Id));
		Assert.Equal(new[] { Last, Second }, (await service.GetExpensesAsync(UserId, new() { Filter = new() { PayerId = UserId } })).Value!.Items.Select(row => row.Id));
		var empty = (await service.GetExpensesAsync(UserId, new() { PageNumber = 99 })).Value!;
		Assert.Equal(3, empty.TotalCount);
		Assert.Empty(empty.Items);
		foreach (var field in Enum.GetValues<ExpenseCategorySortBy>())
		{
			var asc = (await service.GetCategoryTotalsAsync(UserId, new(), field, false)).Value!;
			var desc = (await service.GetCategoryTotalsAsync(UserId, new(), field, true)).Value!;
			Assert.Equal(asc.Select(row => row.CategoryId).Reverse(), desc.Select(row => row.CategoryId));
		}
	}

	[PostgresFact]
	public async Task FiltersNeverGrantAccessAndFormerMemberSeesOnlyTheirHistoricalExpenses()
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		Assert.Equal(ExpenseErrorCode.NotFound, (await service.GetTotalsAsync(UserId, new() { GroupId = PrivateGroup, UserId = Other })).Error);
		Assert.Equal(0m, (await service.GetTotalsAsync(UserId, new() { UserId = Third })).Value!.Amount);
		Assert.Equal(ExpenseErrorCode.NotFound, (await service.GetCategoryTotalsAsync(Third, new() { GroupId = Group })).Error);
		await ExecuteAsync("""
			UPDATE grouping.group_user SET is_member = false WHERE user_id = '10000000-0000-0000-0000-000000000001';
			DELETE FROM expense.expense_share WHERE fk_expense_id = '71000000-0000-0000-0000-000000000002';
			INSERT INTO expense.expense_share (fk_expense_id, fk_user_id, percentage)
			VALUES ('71000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000002', 100);
			UPDATE expense.expense SET fk_payer_id = '10000000-0000-0000-0000-000000000002' WHERE _id = '71000000-0000-0000-0000-000000000002';
			""");
		var history = (await service.GetExpensesAsync(UserId, new() { Filter = new() { GroupId = Group } })).Value!;
		Assert.Equal(new[] { Last, First }, history.Items.Select(row => row.Id));
		Assert.Equal(120m, (await service.GetTotalsAsync(UserId, new() { GroupId = Group })).Value!.Amount);
	}

	[PostgresFact]
	public async Task InvalidQueriesReturnValidationErrorsAndEmptyResultsHaveZeroTotals()
	{
		await PrepareAsync();
		await using var db = OpenExpense();
		await using var grouping = OpenGrouping();
		var service = Service(db, grouping);
		foreach (var filter in new[]
		{
			new ExpenseReportFilter { UserId = Guid.Empty },
			new ExpenseReportFilter { DateFrom = new(2026, 12, 1), DateTo = new(2026, 11, 30) },
			new ExpenseReportFilter { MinAmount = 2, MaxAmount = 1 },
			new ExpenseReportFilter { MinAmount = 0.001m },
			new ExpenseReportFilter { MaxAmount = -1 },
			new ExpenseReportFilter { Search = new string('x', 201) }
		})
		{
			Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetTotalsAsync(UserId, filter)).Error);
			Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetExpensesAsync(UserId, new() { Filter = filter })).Error);
			Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetCategoryTotalsAsync(UserId, filter)).Error);
			Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetTimelineAsync(UserId, new() { Filter = filter })).Error);
		}
		Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetExpensesAsync(UserId, new() { PageSize = 101 })).Error);
		Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetExpensesAsync(UserId, new() { PageNumber = int.MaxValue })).Error);
		Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetExpensesAsync(UserId, new() { SortBy = (ExpenseReportSortBy)99 })).Error);
		Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetCategoryTotalsAsync(UserId, new(), (ExpenseCategorySortBy)99)).Error);
		Assert.Equal(ExpenseErrorCode.InvalidExpense, (await service.GetTimelineAsync(UserId, new() { Period = (ExpenseReportPeriod)99 })).Error);
		Assert.Equal(ExpenseErrorCode.Forbidden, (await service.GetTotalsAsync(Guid.Empty, new())).Error);
		var empty = new ExpenseReportFilter { DateFrom = new(2030, 1, 1) };
		Assert.Equal(new ExpenseReportTotals(0, 0m, 0m, 0m), (await service.GetTotalsAsync(UserId, empty)).Value);
		Assert.Empty((await service.GetCategoryTotalsAsync(UserId, empty)).Value!);
		Assert.Empty((await service.GetTimelineAsync(UserId, new() { Filter = empty })).Value!);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetTotalsAsync(UserId, new(), cancellation.Token));
	}

	private ExpenseDbContext OpenExpense() => new(new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(ConnectionString).Options);
	private GroupingDbContext OpenGrouping() => new(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
	private static ExpenseReadService Service(ExpenseDbContext db, GroupingDbContext grouping) =>
		new(new ExpenseRepository(db), new GroupExpenseMetadataLookup(new GroupRepository(grouping)), new(new ExpenseCalculationService()));

	private async Task PrepareAsync()
	{
		await SeedAsync();
		await ExecuteAsync("DELETE FROM expense.expense_share; DELETE FROM expense.expense_item; DELETE FROM expense.expense;");
		await using var db = OpenExpense();
		Add(db, First, Group, Other, new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), "A purchase", "%_!", 40, [(Food, 60m), (Cleaning, 40m)]);
		Add(db, Second, Group, UserId, new(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc), "B purchase", "Zeta", 50, [(Food, 50m)]);
		Add(db, Last, Group, UserId, new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), "C purchase", null, 100, [(Food, 20m)]);
		Add(db, Guid.NewGuid(), PrivateGroup, Other, new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), "Private", null, 0, [(Food, 900m)]);
		await db.SaveChangesAsync();
		await ExecuteAsync("""
			UPDATE expense.expense SET fk_receipt_id = '60000000-0000-0000-0000-000000000001',
				fk_bank_transaction_id = '50000000-0000-0000-0000-000000000001'
			WHERE _id = '71000000-0000-0000-0000-000000000001';
			""");
	}

	private static void Add(ExpenseDbContext db, Guid id, Guid group, Guid payer, DateTime date, string name,
		string? merchant, decimal percentage, (Guid Category, decimal Amount)[] items)
	{
		db.Expenses.Add(new Models.Expense
		{
			Id = id,
			FkGroupId = group,
			FkPayerId = payer,
			Name = name,
			Merchant = merchant,
			TransactionTime = date,
			Amount = items.Sum(item => item.Amount),
			ExpenseItems = items.Select(item => new ExpenseItem
			{
				Id = Guid.NewGuid(),
				FkExpenseId = id,
				FkCategoryId = item.Category,
				Name = "Item",
				UnitPrice = item.Amount,
				Quantity = 1m
			}).ToList()
		});
		if (percentage > 0) db.ExpenseShares.Add(new ExpenseShare { FkExpenseId = id, FkUserId = UserId, Percentage = percentage });
		if (percentage < 100) db.ExpenseShares.Add(new ExpenseShare { FkExpenseId = id, FkUserId = Other, Percentage = 100 - percentage });
	}
}
