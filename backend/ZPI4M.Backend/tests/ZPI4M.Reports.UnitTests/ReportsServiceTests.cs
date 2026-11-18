using System.ComponentModel.DataAnnotations;
using Xunit;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Reports.Services.Reports;
using ZPI4M.Reports.TestSupport;
using static ZPI4M.Reports.TestSupport.ReportData;

namespace ZPI4M.Reports.UnitTests;

public sealed class ReportsServiceTests
{
	[Theory]
	[InlineData("1969-12-31", "1970-01-01", "DateFrom")]
	[InlineData("1970-01-01", "1969-12-31", "DateTo")]
	[InlineData("1969-12-30", "1969-12-31", "DateFrom")]
	[InlineData("0001-01-01", "1970-01-01", "DateFrom")]
	[InlineData("2026-11-30", "2026-11-01", "DateFrom,DateTo")]
	public async Task AllFourMethodsRejectInvalidDatesBeforeExpense(string from, string to, string fields)
	{
		var source = new ReportsSourceStub();
		var service = new ReportsService(source, Metadata());
		var dateFrom = DateOnly.ParseExact(from, "yyyy-MM-dd");
		var dateTo = DateOnly.ParseExact(to, "yyyy-MM-dd");
		Func<Task>[] calls = [
			() => service.GetCategoryBreakdownAsync(Actor, new() { DateFrom = dateFrom, DateTo = dateTo, Scope = ExpenseAggregationScope.Personal }),
			() => service.GetExpenseTotalsByPeriodAndCategoryAsync(Actor, new() { DateFrom = dateFrom, DateTo = dateTo, Scope = ExpenseAggregationScope.Personal }),
			() => service.GetExpenseTotalsByDayOfWeekAsync(Actor, new() { DateFrom = dateFrom, DateTo = dateTo, Scope = ExpenseAggregationScope.Personal }),
			() => service.GetDailyExpenseTotalsAsync(Actor, new() { DateFrom = dateFrom, DateTo = dateTo, Scope = ExpenseAggregationScope.Personal })
		];
		foreach (var call in calls)
		{
			var error = await Assert.ThrowsAsync<ValidationException>(call);
			Assert.Equal(fields.Split(','), error.ValidationResult.MemberNames);
		}
		Assert.Equal(0, source.Calls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task AllFourMethodsForwardFiltersAndCancellationUnchanged(bool group)
	{
		var source = new ReportsSourceStub();
		var service = new ReportsService(source, Metadata(Food));
		Guid[] ids = [Food, Food];
		var from = new DateOnly(2026, 1, 15);
		var to = new DateOnly(2026, 3, 20);
		var scope = group ? ExpenseAggregationScope.Group : ExpenseAggregationScope.Personal;
		Guid? id = group ? Group : null;
		using var cancel = new CancellationTokenSource();
		await service.GetCategoryBreakdownAsync(Actor, new() { DateFrom = from, DateTo = to, Scope = scope, GroupId = id, CategoryIds = ids }, cancel.Token);
		await service.GetExpenseTotalsByPeriodAndCategoryAsync(Actor, new() { DateFrom = from, DateTo = to, Scope = scope, GroupId = id, CategoryIds = ids }, cancel.Token);
		await service.GetExpenseTotalsByDayOfWeekAsync(Actor, new() { DateFrom = from, DateTo = to, Scope = scope, GroupId = id, CategoryIds = ids }, cancel.Token);
		await service.GetDailyExpenseTotalsAsync(Actor, new() { DateFrom = from, DateTo = to, Scope = scope, GroupId = id, CategoryIds = ids }, cancel.Token);
		Assert.Equal(4, source.Calls);
		Assert.Equal(Actor, source.Actor);
		Assert.Equal(from, source.From);
		Assert.Equal(to, source.To);
		Assert.Equal(scope, source.Scope);
		Assert.Equal(id, source.GroupId);
		Assert.Same(ids, source.CategoryIds);
		Assert.Equal(cancel.Token, source.Token);
	}

	[Theory]
	[InlineData(8)]
	[InlineData(9)]
	[InlineData(12)]
	public async Task DonutKeepsTopEightAndOtherPreservesSignedTotalsAndOrdering(int count)
	{
		var ids = Enumerable.Range(1, count).Select(Category).ToArray();
		var source = new ReportsSourceStub { Categories = ids.Select((id, i) => new ExpenseCategoryBreakdownItem(id, 10m - i)).ToArray() };
		var response = await new ReportsService(source, Metadata(ids)).GetCategoryBreakdownAsync(Actor,
			new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 1), Scope = ExpenseAggregationScope.Personal, CategoryIds = ids });
		Assert.Equal(count <= 8 ? count : 9, response.Items.Count);
		Assert.Equal(source.Categories.Sum(row => row.TotalAmount), response.Items.Sum(row => row.TotalAmount));
		Assert.Equal(response.Items.Select(row => row.TotalAmount).OrderDescending(), response.Items.Select(row => row.TotalAmount));
		Assert.Equal(count > 8 ? 1 : 0, response.Items.Count(row => row.CategoryId is null));
		if (count > 8)
		{
			var other = Assert.Single(response.Items, row => row.CategoryId is null);
			Assert.Equal("Pozostałe", other.Name);
			Assert.Equal(source.Categories.Skip(8).Sum(row => row.TotalAmount), other.TotalAmount);
		}
	}

	[Theory]
	[InlineData(2025, 1, 2026, 12, ExpenseTotalsGranularity.Month)]
	[InlineData(2025, 1, 2027, 1, ExpenseTotalsGranularity.Quarter)]
	[InlineData(2023, 1, 2026, 12, ExpenseTotalsGranularity.Quarter)]
	[InlineData(2023, 1, 2027, 1, ExpenseTotalsGranularity.Year)]
	[InlineData(9999, 12, 9999, 12, ExpenseTotalsGranularity.Month)]
	[InlineData(1970, 1, 9999, 12, ExpenseTotalsGranularity.Year)]
	public async Task CalendarMonthsIncludingPartialEdgesDetermineGranularity(int y1, int m1, int y2, int m2, ExpenseTotalsGranularity expected)
	{
		var source = new ReportsSourceStub();
		var result = await new ReportsService(source, Metadata(Food)).GetExpenseTotalsByPeriodAndCategoryAsync(Actor,
			new() { DateFrom = new(y1, m1, 15), DateTo = new(y2, m2, 15), Scope = ExpenseAggregationScope.Personal, CategoryIds = [Food] });
		Assert.Equal(expected, result.Granularity);
		Assert.Equal(expected, source.Granularity);
		Assert.All(Assert.Single(result.Series).Items, row => Assert.Equal(0m, row.TotalAmount));
	}

	[Fact]
	public async Task PeriodsChooseTopCategoriesAcrossWholeRangeAndFillIdenticalPeriods()
	{
		var ids = Enumerable.Range(1, 10).Select(Category).ToArray();
		var rows = ids.Select((id, i) => new ExpensePeriodCategoryTotal(new(2026, 1, 1), id, 100m - i * 10)).ToList();
		rows.Add(new(new(2026, 2, 1), ids[9], 200m));
		var source = new ReportsSourceStub { Periods = rows };
		var response = await new ReportsService(source, Metadata(ids)).GetExpenseTotalsByPeriodAndCategoryAsync(Actor,
			new() { DateFrom = new(2026, 1, 15), DateTo = new(2026, 3, 20), Scope = ExpenseAggregationScope.Personal, CategoryIds = ids });
		Assert.Equal(9, response.Series.Count);
		Assert.Equal(ids[9], response.Series[0].CategoryId);
		Assert.All(response.Series, series => Assert.Equal(
			new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1) }, series.Items.Select(row => row.Period)));
		Assert.Equal(new[] { 50m, 0m, 0m }, Assert.Single(response.Series, series => series.CategoryId is null).Items.Select(row => row.TotalAmount));
		Assert.Equal(rows.Sum(row => row.TotalAmount), response.Series.Sum(series => series.Items.Sum(row => row.TotalAmount)));
		Assert.All(response.Series, series => Assert.Equal(0m, series.Items[^1].TotalAmount));
	}

	[Fact]
	public async Task WeekdayAveragesCountAllOccurrencesAndHandleAbsentWeekdays()
	{
		var source = new ReportsSourceStub { Weekdays = [new(DayOfWeek.Sunday, 12.34m), new(DayOfWeek.Monday, 10m), new(DayOfWeek.Tuesday, 3m)] };
		var service = new ReportsService(source, Metadata());
		var result = await service.GetExpenseTotalsByDayOfWeekAsync(Actor,
			new() { DateFrom = new(2026, 11, 1), DateTo = new(2026, 11, 30), Scope = ExpenseAggregationScope.Personal, CategoryIds = [Food] });
		Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }, result.Items.Select(row => row.DayOfWeek));
		Assert.Equal(new[] { 2m, .75m, 0m, 0m, 0m, 0m, 2.468m }, result.Items.Select(row => row.AverageAmount));
		source.Weekdays = [new(DayOfWeek.Monday, 10m)];
		result = await service.GetExpenseTotalsByDayOfWeekAsync(Actor,
			new() { DateFrom = new(2026, 11, 2), DateTo = new(2026, 11, 2), Scope = ExpenseAggregationScope.Personal, CategoryIds = [Food] });
		Assert.Equal(10m, result.Items[0].AverageAmount);
		Assert.All(result.Items.Skip(1), row => Assert.Equal(0m, row.AverageAmount));
	}

	[Theory]
	[InlineData(1970, 1, 1, 1970, 1, 3)]
	[InlineData(2024, 2, 28, 2024, 3, 1)]
	[InlineData(2026, 12, 31, 2027, 1, 2)]
	[InlineData(9999, 12, 29, 9999, 12, 31)]
	public async Task DailyCalendarFillsInclusiveDatesWithoutOverflow(int y1, int m1, int d1, int y2, int m2, int d2)
	{
		var from = new DateOnly(y1, m1, d1);
		var to = new DateOnly(y2, m2, d2);
		var source = new ReportsSourceStub { Days = [new(to, -.01m)] };
		var result = await new ReportsService(source, Metadata()).GetDailyExpenseTotalsAsync(Actor,
			new() { DateFrom = from, DateTo = to, Scope = ExpenseAggregationScope.Personal, CategoryIds = [Food] });
		Assert.Equal(3, result.Items.Count);
		Assert.Equal(from, result.Items[0].Date);
		Assert.Equal(to, result.Items[^1].Date);
		Assert.Equal(new[] { 0m, 0m, -.01m }, result.Items.Select(row => row.TotalAmount));
	}

	[Fact]
	public async Task ExplicitEmptySelectionReturnsEmptyForEveryReport()
	{
		var source = new ReportsSourceStub();
		var service = new ReportsService(source, Metadata());
		Assert.Empty((await service.GetCategoryBreakdownAsync(Actor, new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 3), Scope = ExpenseAggregationScope.Personal, CategoryIds = [] })).Items);
		Assert.Empty((await service.GetExpenseTotalsByPeriodAndCategoryAsync(Actor, new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 3), Scope = ExpenseAggregationScope.Personal, CategoryIds = [] })).Series);
		Assert.Empty((await service.GetExpenseTotalsByDayOfWeekAsync(Actor, new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 3), Scope = ExpenseAggregationScope.Personal, CategoryIds = [] })).Items);
		Assert.Empty((await service.GetDailyExpenseTotalsAsync(Actor, new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 3), Scope = ExpenseAggregationScope.Personal, CategoryIds = [] })).Items);
		Assert.Equal(4, source.Calls); // Access errors from Expense must not be skipped.
	}

	[Theory]
	[InlineData(ExpenseErrorCode.NotFound)]
	[InlineData(ExpenseErrorCode.Forbidden)]
	[InlineData(ExpenseErrorCode.Conflict)]
	[InlineData(ExpenseErrorCode.TemporarilyUnavailable)]
	[InlineData(ExpenseErrorCode.InvalidExpense)]
	public async Task SourceErrorsArePreservedForAllReports(ExpenseErrorCode code)
	{
		var source = new ReportsSourceStub { Error = code };
		var service = new ReportsService(source, Metadata());
		Func<Task>[] calls = [
			() => service.GetCategoryBreakdownAsync(Actor, new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 1), Scope = ExpenseAggregationScope.Personal, CategoryIds = [] }),
			() => service.GetExpenseTotalsByPeriodAndCategoryAsync(Actor, new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 1), Scope = ExpenseAggregationScope.Personal, CategoryIds = [] }),
			() => service.GetExpenseTotalsByDayOfWeekAsync(Actor, new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 1), Scope = ExpenseAggregationScope.Personal, CategoryIds = [] }),
			() => service.GetDailyExpenseTotalsAsync(Actor, new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 1), Scope = ExpenseAggregationScope.Personal, CategoryIds = [] })
		];
		foreach (var call in calls)
		{
			var error = await Assert.ThrowsAsync<ReportsReadException>(call);
			Assert.Equal(code, error.Error);
			Assert.Equal("Source validation", Assert.Single(error.ValidationErrors).ErrorMessage);
		}
	}

	[Fact]
	public async Task InvalidInputsAndCancellationDoNotReachExpense()
	{
		var source = new ReportsSourceStub();
		var service = new ReportsService(source, Metadata());
		foreach (var (scope, group, ids) in new (ExpenseAggregationScope, Guid?, IReadOnlyList<Guid>)[]
		{
			(ExpenseAggregationScope.Personal, Group, [Food]), (ExpenseAggregationScope.Group, null, [Food]),
			(ExpenseAggregationScope.Group, Guid.Empty, [Food]), ((ExpenseAggregationScope)99, null, [Food]),
			(ExpenseAggregationScope.Personal, null, [Guid.Empty]), (ExpenseAggregationScope.Personal, null, null!)
		})
			await Assert.ThrowsAsync<ValidationException>(() => service.GetCategoryBreakdownAsync(Actor,
				new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 1), Scope = scope, GroupId = group, CategoryIds = ids }));
		await Assert.ThrowsAsync<ValidationException>(() => service.GetCategoryBreakdownAsync(Actor,
			new() { DateFrom = new(2026, 2, 1), DateTo = new(2026, 1, 1), Scope = ExpenseAggregationScope.Personal, CategoryIds = [Food] }));
		Assert.Equal(ExpenseErrorCode.Forbidden, (await Assert.ThrowsAsync<ReportsReadException>(() => service.GetCategoryBreakdownAsync(Guid.Empty,
			new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 1), Scope = ExpenseAggregationScope.Personal, CategoryIds = [] }))).Error);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetDailyExpenseTotalsAsync(Actor,
			new() { DateFrom = new(2026, 1, 1), DateTo = new(2026, 1, 1), Scope = ExpenseAggregationScope.Personal, CategoryIds = [] }, new CancellationToken(true)));
		Assert.Equal(0, source.Calls);
	}
}
