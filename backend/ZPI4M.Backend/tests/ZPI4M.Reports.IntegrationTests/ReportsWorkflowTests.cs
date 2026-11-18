using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.IntegrationTests;
using ZPI4M.Expense.Models;
using ZPI4M.Reports.DTOs.CategoryBreakdown;
using ZPI4M.Reports.DTOs.DailyTotals;
using ZPI4M.Reports.DTOs.DayOfWeekTotals;
using ZPI4M.Reports.DTOs.PeriodCategoryTotals;

namespace ZPI4M.Reports.IntegrationTests;

public sealed class ReportsWorkflowTests : PostgresFixture
{
	private static readonly Guid Group = Guid.Parse("20000000-0000-0000-0000-000000000001");
	private static readonly Guid PrivateGroup = Guid.Parse("20000000-0000-0000-0000-000000000002");
	private static readonly Guid PersonalGroup = Guid.Parse("20000000-0000-0000-0000-000000000003");
	private static readonly Guid Other = Guid.Parse("10000000-0000-0000-0000-000000000002");
	private static readonly Guid Food = Guid.Parse("30000000-0000-0000-0000-000000000001");
	private static readonly Guid Cleaning = Guid.Parse("30000000-0000-0000-0000-000000000010");
	private static readonly string[] Routes = ["category-breakdown", "period-category-totals", "day-of-week-totals", "daily-totals"];
	private static string Query(bool group = false, string? categories = null, string from = "2026-11-01", string to = "2026-11-30") =>
		$"dateFrom={from}&dateTo={to}&scope=" + (group ? $"group&groupId={Group}" : "personal") + "&" +
		(categories ?? $"categoryIds={Food}&categoryIds={Cleaning}");

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task AllReportsUseRealItemAggregatesAndPersonalSharesOrGroupCosts(bool group)
	{
		await PrepareAsync();
		await using var host = await ReportsTestHost.StartAsync(ConnectionString, UserId);
		var query = Query(group);
		var categories = await GetAsync<CategoryBreakdownResponse>(host, Routes[0], query);
		Assert.Equal(group ? 110m : 69m, categories.Items.Single(row => row.CategoryId == Food).TotalAmount);
		Assert.Equal(group ? 120m : 16m, categories.Items.Single(row => row.CategoryId == Cleaning).TotalAmount);
		var periods = await GetAsync<ExpenseTotalsByPeriodAndCategoryResponse>(host, Routes[1], query);
		Assert.Equal(2, periods.Series.Count);
		Assert.Equal(group ? 230m : 85m, periods.Series.Sum(series => Assert.Single(series.Items).TotalAmount));
		var daily = await GetAsync<DailyTotalsResponse>(host, Routes[3], query);
		Assert.Equal(30, daily.Items.Count);
		Assert.Equal(group ? 100m : 40m, daily.Items[0].TotalAmount);
		Assert.Equal(group ? 0m : 20m, daily.Items[9].TotalAmount);
		Assert.Equal(group ? 80m : 0m, daily.Items[14].TotalAmount);
		Assert.Equal(group ? 50m : 25m, daily.Items[29].TotalAmount);
		Assert.Equal(categories.Items.Sum(row => row.TotalAmount), daily.Items.Sum(row => row.TotalAmount));
		var weekdays = await GetAsync<ExpenseTotalsByDayOfWeekResponse>(host, Routes[2], query);
		Assert.Equal(7, weekdays.Items.Count);
		Assert.Equal(group ? 10m : 5m, weekdays.Items[0].AverageAmount);
		Assert.Equal(group ? 0m : 5m, weekdays.Items[1].AverageAmount);
		Assert.Equal(group ? 36m : 8m, weekdays.Items[6].AverageAmount);
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task OneCategoryAndDuplicatesCountOnlyItsItemsAcrossAllReports(bool group)
	{
		await PrepareAsync();
		await using var host = await ReportsTestHost.StartAsync(ConnectionString, UserId);
		var query = Query(group, $"categoryIds={Food}&categoryIds={Food}");
		var expected = group ? 110m : 69m;
		var categories = await GetAsync<CategoryBreakdownResponse>(host, Routes[0], query);
		Assert.Equal(expected, Assert.Single(categories.Items).TotalAmount);
		var periods = await GetAsync<ExpenseTotalsByPeriodAndCategoryResponse>(host, Routes[1], query);
		Assert.Equal(expected, Assert.Single(Assert.Single(periods.Series).Items).TotalAmount);
		Assert.Equal(expected, (await GetAsync<DailyTotalsResponse>(host, Routes[3], query)).Items.Sum(row => row.TotalAmount));
		var weekdays = await GetAsync<ExpenseTotalsByDayOfWeekResponse>(host, Routes[2], query);
		Assert.Equal(expected, weekdays.Items.Sum(row => row.AverageAmount *
			(row.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Sunday ? 5 : 4)));
	}

	[PostgresTheory]
	[InlineData("2025-01-15", "2026-12-31", "Month", 24)]
	[InlineData("2025-01-15", "2027-01-31", "Quarter", 9)]
	[InlineData("2023-01-15", "2026-12-31", "Quarter", 16)]
	[InlineData("2023-01-15", "2027-01-31", "Year", 5)]
	public async Task ThresholdsChooseCalendarAggregationAndFillEmptyPeriods(string from, string to, string granularity, int count)
	{
		await PrepareAsync();
		await using var host = await ReportsTestHost.StartAsync(ConnectionString, UserId);
		var result = await GetAsync<ExpenseTotalsByPeriodAndCategoryResponse>(host, Routes[1], Query(true, from: from, to: to));
		Assert.Equal(granularity, result.Granularity.ToString());
		Assert.All(result.Series, series => Assert.Equal(count, series.Items.Count));
		Assert.Equal(257m, result.Series.Sum(series => series.Items.Sum(row => row.TotalAmount)));
		Assert.Equal(result.Series[0].Items.Select(row => row.Period), result.Series[1].Items.Select(row => row.Period));
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task EmptySelectionIsEmptyForEveryReportAndDoesNotBypassGroupAccess(bool group)
	{
		await PrepareAsync();
		await using var host = await ReportsTestHost.StartAsync(ConnectionString, UserId);
		foreach (var route in Routes)
		{
			var json = await GetAsync<JsonElement>(host, route, Query(group, ""));
			Assert.Equal(0, json.GetProperty(route == Routes[1] ? "series" : "items").GetArrayLength());
			using var denied = await host.Client.GetAsync($"/api/reports/{route}?" +
				Query(true, "").Replace(Group.ToString(), PrivateGroup.ToString()));
			Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
		}
	}

	[PostgresFact]
	public async Task StandardBindingRejectsInvalidAndEmptyCategoryValuesAcrossAllReports()
	{
		await PrepareAsync();
		await using var host = await ReportsTestHost.StartAsync(ConnectionString, UserId);
		foreach (var route in Routes)
			foreach (var categories in new[] { "categoryIds=", "categoryIds=abc", $"categoryIds={Food}&categoryIds=", $"categoryIds=&categoryIds={Food}", "categoryIds=&categoryIds=" })
			{
				using var response = await host.Client.GetAsync($"/api/reports/{route}?" + Query(categories: categories));
				Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
				Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
			}
	}

	[PostgresFact]
	public async Task ForeignAndMissingGroupsAreDeniedAndActorQueryCannotExpandAccess()
	{
		await PrepareAsync();
		await using var host = await ReportsTestHost.StartAsync(ConnectionString, UserId);
		foreach (var group in new[] { PrivateGroup, Guid.NewGuid() })
			foreach (var route in Routes)
			{
				using var response = await host.Client.GetAsync($"/api/reports/{route}?" +
					Query(true).Replace(Group.ToString(), group.ToString()) + $"&actor={Other}");
				Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
				Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
			}
	}

	[PostgresFact]
	public async Task FormerMemberSeesOnlyRelatedPurchases()
	{
		await PrepareAsync();
		await ExecuteAsync($"UPDATE grouping.group_user SET is_member = false WHERE user_id = '{UserId}' AND group_id = '{Group}'");
		await using var host = await ReportsTestHost.StartAsync(ConnectionString, UserId);
		var categories = await GetAsync<CategoryBreakdownResponse>(host, Routes[0], Query(true));
		Assert.Equal(150m, categories.Items.Sum(row => row.TotalAmount)); // The unrelated 80 is hidden.
		Assert.Equal(150m, (await GetAsync<DailyTotalsResponse>(host, Routes[3], Query(true))).Items.Sum(row => row.TotalAmount));
	}

	[PostgresTheory]
	[InlineData(1970, 1, 1)]
	[InlineData(2024, 2, 29)]
	[InlineData(9999, 12, 31)]
	public async Task InclusiveEdgesLeapDayAndExtremeDatesWorkAcrossAllReports(int year, int month, int day)
	{
		await PrepareAsync();
		var date = new DateOnly(year, month, day);
		await using (var db = OpenExpense())
		{
			Add(db, Group, Other, date.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc).AddTicks(9999990),
				50m, (Food, .03m));
			await db.SaveChangesAsync();
		}
		await using var host = await ReportsTestHost.StartAsync(ConnectionString, UserId);
		var query = Query(categories: $"categoryIds={Food}", from: date.ToString("yyyy-MM-dd"), to: date.ToString("yyyy-MM-dd"));
		Assert.Equal(.02m, Assert.Single((await GetAsync<CategoryBreakdownResponse>(host, Routes[0], query)).Items).TotalAmount);
		Assert.Equal(.02m, Assert.Single(Assert.Single((await GetAsync<ExpenseTotalsByPeriodAndCategoryResponse>(host, Routes[1], query)).Series).Items).TotalAmount);
		var daily = Assert.Single((await GetAsync<DailyTotalsResponse>(host, Routes[3], query)).Items);
		Assert.Equal(date, daily.Date);
		Assert.Equal(.02m, daily.TotalAmount);
		var weekdays = (await GetAsync<ExpenseTotalsByDayOfWeekResponse>(host, Routes[2], query)).Items;
		Assert.Equal(.02m, weekdays.Single(row => row.DayOfWeek == date.DayOfWeek).AverageAmount);
	}

	[PostgresFact]
	public async Task ItemRoundingNegativeAmountsAndZeroFilledDaysStayConsistent()
	{
		await PrepareAsync();
		await ClearAsync();
		await using (var db = OpenExpense())
		{
			Add(db, Group, Other, Utc(2026, 11, 1), 50m, (Food, .01m), (Cleaning, .02m));
			Add(db, Group, Other, Utc(2026, 11, 2), 50m, (Food, 10m), (Cleaning, -2m));
			await db.SaveChangesAsync();
		}
		await using var host = await ReportsTestHost.StartAsync(ConnectionString, UserId);
		var categories = (await GetAsync<CategoryBreakdownResponse>(host, Routes[0], Query())).Items;
		Assert.Equal(5.01m, categories.Single(row => row.CategoryId == Food).TotalAmount);
		Assert.Equal(-.99m, categories.Single(row => row.CategoryId == Cleaning).TotalAmount);
		var daily = (await GetAsync<DailyTotalsResponse>(host, Routes[3], Query())).Items;
		Assert.Equal(new[] { .02m, 4m, 0m }, daily.Take(3).Select(row => row.TotalAmount));
		Assert.Equal(4.02m, daily.Sum(row => row.TotalAmount));
	}

	[PostgresFact]
	public async Task NineCategoriesProduceStableOtherWithPreservedTotals()
	{
		await PrepareAsync();
		await ClearAsync();
		var ids = Enumerable.Range(1, 9).Select(i => Guid.Parse($"39000000-0000-0000-0000-{i:D12}")).ToArray();
		await using (var db = OpenExpense())
		{
			for (var i = 0; i < ids.Length; i++) db.Categories.Add(new() { Id = ids[i], Name = "Reports category " + i, Description = "", Icon = "tag" });
			Add(db, Group, UserId, Utc(2026, 11, 1), 100m, ids.Select((id, i) => (id, 100m - i * 10)).ToArray());
			Add(db, Group, UserId, Utc(2026, 12, 1), 100m, (ids[8], 200m));
			await db.SaveChangesAsync();
		}
		await using var host = await ReportsTestHost.StartAsync(ConnectionString, UserId);
		var query = Query(true, string.Join("&", ids.Select(id => "categoryIds=" + id)), to: "2026-12-31");
		var categories = (await GetAsync<CategoryBreakdownResponse>(host, Routes[0], query)).Items;
		Assert.Equal(9, categories.Count);
		Assert.Equal(740m, categories.Sum(row => row.TotalAmount));
		Assert.Single(categories, row => row.CategoryId is null);
		var periods = (await GetAsync<ExpenseTotalsByPeriodAndCategoryResponse>(host, Routes[1], query)).Series;
		Assert.Equal(9, periods.Count);
		Assert.Equal(740m, periods.Sum(series => series.Items.Sum(row => row.TotalAmount)));
		Assert.All(periods, series => Assert.Equal(2, series.Items.Count));
		Assert.Equal(new[] { 30m, 0m }, Assert.Single(periods, series => series.CategoryId is null).Items.Select(row => row.TotalAmount));
	}

	private async Task PrepareAsync()
	{
		await SeedAsync();
		await ClearAsync();
		await ExecuteAsync($"""
			INSERT INTO grouping."group" (id, name, admin_user_id, currency_code)
			VALUES ('{PersonalGroup}', 'Personal reports', '{UserId}', 'PLN');
			INSERT INTO grouping.group_user (user_id, group_id, joined_at)
			VALUES ('{UserId}', '{PersonalGroup}', TIMESTAMPTZ '2020-01-01 00:00:00Z');
			""");
		await using var db = OpenExpense();
		Add(db, Group, Other, Utc(2026, 11, 1), 40m, (Food, 60m), (Cleaning, 40m));
		Add(db, Group, UserId, Utc(2026, 11, 30).AddHours(23).AddMinutes(59).AddSeconds(59), 50m, (Food, 50m));
		Add(db, Group, Other, Utc(2026, 11, 15), 0m, (Cleaning, 80m));
		Add(db, PersonalGroup, UserId, Utc(2026, 11, 10), 100m, (Food, 20m));
		Add(db, PrivateGroup, Other, Utc(2026, 11, 1), 0m, (Food, 900m));
		Add(db, Group, UserId, Utc(2026, 12, 1), 100m, (Food, 20m));
		Add(db, Group, UserId, Utc(2026, 10, 31), 100m, (Food, 7m));
		await db.SaveChangesAsync();
	}
	private Task ClearAsync() => ExecuteAsync("DELETE FROM expense.expense_share; DELETE FROM expense.expense_item; DELETE FROM expense.expense;");
	private ExpenseDbContext OpenExpense() => new(new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(ConnectionString).Options);
	private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);
	private static void Add(ExpenseDbContext db, Guid group, Guid payer, DateTime date, decimal percentage, params (Guid Id, decimal Amount)[] items)
	{
		var id = Guid.NewGuid();
		db.Expenses.Add(new ZPI4M.Expense.Models.Expense
		{
			Id = id,
			FkGroupId = group,
			FkPayerId = payer,
			Name = "Synthetic report purchase",
			TransactionTime = date,
			Amount = items.Sum(item => item.Amount),
			ExpenseItems = items.Select(item => new ExpenseItem
			{ Id = Guid.NewGuid(), FkExpenseId = id, FkCategoryId = item.Id, Name = "Item", UnitPrice = item.Amount, Quantity = 1m }).ToList()
		});
		if (percentage > 0m) db.ExpenseShares.Add(new() { FkExpenseId = id, FkUserId = UserId, Percentage = percentage });
		if (percentage < 100m) db.ExpenseShares.Add(new() { FkExpenseId = id, FkUserId = Other, Percentage = 100m - percentage });
	}
	private static async Task<T> GetAsync<T>(ReportsTestHost host, string route, string query)
	{
		using var response = await host.Client.GetAsync($"/api/reports/{route}?{query}");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
		return (await response.Content.ReadFromJsonAsync<T>())!;
	}
}
