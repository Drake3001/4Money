using System.ComponentModel.DataAnnotations;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Reports.DTOs;
using ZPI4M.Reports.DTOs.CategoryBreakdown;
using ZPI4M.Reports.DTOs.DailyTotals;
using ZPI4M.Reports.DTOs.DayOfWeekTotals;
using ZPI4M.Reports.DTOs.PeriodCategoryTotals;

namespace ZPI4M.Reports.Services.Reports;

public sealed class ReportsService(IExpenseAggregationService expenses, IExpenseService expenseMetadata) : IReportsService
{
	private static readonly DateOnly MinimumDate = new(1970, 1, 1);
	private const int VisibleCategories = 8;
	private const string OtherName = "Pozostałe";

	public async Task<CategoryBreakdownResponse> GetCategoryBreakdownAsync(Guid actor, CategoryBreakdownRequest request,
		CancellationToken cancellationToken = default)
	{
		Validate(actor, request, cancellationToken);
		var rows = Read(await expenses.GetCategoryBreakdownAsync(actor, request.DateFrom, request.DateTo, request.Scope,
			request.GroupId, request.CategoryIds, cancellationToken));
		if (request.CategoryIds.Count == 0 || rows.Count == 0) return new([]);
		var names = await CategoryNamesAsync(cancellationToken);
		var ranked = RankCategories(rows.Select(row => (row.CategoryId, row.TotalAmount)), names);
		if (ranked.Length <= VisibleCategories) return new(ranked);
		var items = ranked.Take(VisibleCategories)
			.Append(new CategoryBreakdownItemResponse(null, OtherName, ranked.Skip(VisibleCategories).Sum(row => row.TotalAmount)));
		return new(items.OrderByDescending(row => row.TotalAmount)
			.ThenBy(row => row.CategoryId?.ToString("D"), StringComparer.Ordinal).ToArray());
	}

	public async Task<ExpenseTotalsByPeriodAndCategoryResponse> GetExpenseTotalsByPeriodAndCategoryAsync(
		Guid actor, ExpenseTotalsByPeriodAndCategoryRequest request, CancellationToken cancellationToken = default)
	{
		Validate(actor, request, cancellationToken);
		var granularity = SelectGranularity(request.DateFrom, request.DateTo);
		var rows = Read(await expenses.GetExpenseTotalsByPeriodAndCategoryAsync(actor, request.DateFrom, request.DateTo,
			request.Scope, request.GroupId, request.CategoryIds, granularity, cancellationToken));
		if (request.CategoryIds.Count == 0) return new([], granularity);
		var names = await CategoryNamesAsync(cancellationToken);
		// Presentation totals only: Expense has already aggregated purchases in SQL.
		var totals = rows.GroupBy(row => row.CategoryId)
			.ToDictionary(category => category.Key, category => category.Sum(row => row.TotalAmount));
		// Selected existing categories remain visible even when no purchases matched.
		var ranked = RankCategories(request.CategoryIds.Distinct().Where(names.ContainsKey)
			.Select(id => (id, totals.GetValueOrDefault(id))), names);
		var shown = ranked.Take(VisibleCategories).ToArray();
		var shownIds = shown.Select(row => row.CategoryId!.Value).ToHashSet();
		var periods = PeriodStarts(request.DateFrom, request.DateTo, granularity, cancellationToken);
		var amounts = rows.ToDictionary(row => (row.Period, row.CategoryId), row => row.TotalAmount);
		var series = shown.Select(category => new ExpensePeriodCategorySeries(category.CategoryId, category.Name,
			periods.Select(period => new ExpensePeriodCategoryItem(period,
				amounts.GetValueOrDefault((period, category.CategoryId!.Value)))).ToArray())).ToList();
		if (ranked.Length > VisibleCategories)
		{
			var other = rows.Where(row => !shownIds.Contains(row.CategoryId)).GroupBy(row => row.Period)
				.ToDictionary(period => period.Key, period => period.Sum(row => row.TotalAmount));
			series.Add(new(null, OtherName, periods.Select(period =>
				new ExpensePeriodCategoryItem(period, other.GetValueOrDefault(period))).ToArray()));
		}
		return new(series, granularity);
	}

	public async Task<ExpenseTotalsByDayOfWeekResponse> GetExpenseTotalsByDayOfWeekAsync(
		Guid actor, ExpenseTotalsByDayOfWeekRequest request, CancellationToken cancellationToken = default)
	{
		Validate(actor, request, cancellationToken);
		var rows = Read(await expenses.GetExpenseTotalsByDayOfWeekAsync(actor, request.DateFrom, request.DateTo, request.Scope,
			request.GroupId, request.CategoryIds, cancellationToken));
		if (request.CategoryIds.Count == 0) return new([]);
		var totals = rows.ToDictionary(row => row.DayOfWeek, row => row.TotalAmount);
		var days = request.DateTo.DayNumber - request.DateFrom.DayNumber + 1;
		var items = Enumerable.Range(0, 7).Select(index =>
		{
			var day = (DayOfWeek)((index + 1) % 7);
			var offset = ((int)day - (int)request.DateFrom.DayOfWeek + 7) % 7;
			var occurrences = days / 7 + (offset < days % 7 ? 1 : 0);
			// Preserve decimal precision; display rounding belongs to the consumer.
			return new ExpenseDayOfWeekItem(day, occurrences == 0 ? 0m : totals.GetValueOrDefault(day) / occurrences);
		}).ToArray();
		return new(items);
	}

	public async Task<DailyTotalsResponse> GetDailyExpenseTotalsAsync(Guid actor, DailyTotalsRequest request,
		CancellationToken cancellationToken = default)
	{
		Validate(actor, request, cancellationToken);
		var rows = Read(await expenses.GetDailyExpenseTotalsAsync(actor, request.DateFrom, request.DateTo, request.Scope,
			request.GroupId, request.CategoryIds, cancellationToken));
		if (request.CategoryIds.Count == 0) return new([]);
		var amounts = rows.ToDictionary(row => row.Date, row => row.TotalAmount);
		var items = new List<DailyTotalsItem>();
		for (var number = request.DateFrom.DayNumber; number <= request.DateTo.DayNumber; number++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var date = DateOnly.FromDayNumber(number);
			items.Add(new(date, amounts.GetValueOrDefault(date)));
		}
		return new(items);
	}

	private async Task<IReadOnlyDictionary<Guid, string>> CategoryNamesAsync(CancellationToken token) =>
		Read(await expenseMetadata.GetCategoriesAsync(token)).ToDictionary(category => category.Id, category => category.Name);

	private static CategoryBreakdownItemResponse[] RankCategories(IEnumerable<(Guid Id, decimal Amount)> totals,
		IReadOnlyDictionary<Guid, string> names) =>
		totals.Select(row => new CategoryBreakdownItemResponse(row.Id, names[row.Id], row.Amount))
			.OrderByDescending(row => row.TotalAmount)
			.ThenBy(row => row.CategoryId!.Value.ToString("D"), StringComparer.Ordinal).ToArray();

	private static ExpenseTotalsGranularity SelectGranularity(DateOnly from, DateOnly to)
	{
		// Count touched calendar months, including partial edges: at most 24 monthly buckets.
		var months = (to.Year - from.Year) * 12 + to.Month - from.Month + 1;
		return months <= 24 ? ExpenseTotalsGranularity.Month
			: months <= 48 ? ExpenseTotalsGranularity.Quarter : ExpenseTotalsGranularity.Year;
	}

	private static IReadOnlyList<DateOnly> PeriodStarts(DateOnly from, DateOnly to, ExpenseTotalsGranularity granularity,
		CancellationToken token)
	{
		var step = granularity switch { ExpenseTotalsGranularity.Month => 1, ExpenseTotalsGranularity.Quarter => 3, _ => 12 };
		var month = granularity switch
		{
			ExpenseTotalsGranularity.Month => from.Month,
			ExpenseTotalsGranularity.Quarter => (from.Month - 1) / 3 * 3 + 1,
			_ => 1
		};
		var current = new DateOnly(from.Year, month, 1);
		var periods = new List<DateOnly>();
		while (current <= to)
		{
			token.ThrowIfCancellationRequested();
			periods.Add(current);
			var monthsLeft = (to.Year - current.Year) * 12 + to.Month - current.Month;
			if (monthsLeft < step) break; // Avoid overflow at year 9999.
			current = current.AddMonths(step);
		}
		return periods;
	}

	private static T Read<T>(ExpenseResult<T> result) where T : notnull =>
		result.IsSuccess ? result.Value! : throw new ReportsReadException(result.Error!.Value, result.ValidationErrors);

	private static void Validate(Guid actor, ReportRequest request, CancellationToken token)
	{
		ArgumentNullException.ThrowIfNull(request);
		token.ThrowIfCancellationRequested();
		if (actor == Guid.Empty) throw new ReportsReadException(ExpenseErrorCode.Forbidden, []);
		if (request.DateFrom < MinimumDate) throw Invalid("DateFrom must be on or after 1970-01-01.", nameof(request.DateFrom));
		if (request.DateTo < MinimumDate) throw Invalid("DateTo must be on or after 1970-01-01.", nameof(request.DateTo));
		if (request.DateFrom > request.DateTo) throw Invalid("DateFrom must not exceed DateTo.", nameof(request.DateFrom), nameof(request.DateTo));
		if (!Enum.IsDefined(request.Scope)) throw Invalid("Unknown report scope.", nameof(request.Scope));
		if (request.Scope == ExpenseAggregationScope.Personal && request.GroupId is not null)
			throw Invalid("Personal scope requires no GroupId.", nameof(request.Scope), nameof(request.GroupId));
		if (request.Scope == ExpenseAggregationScope.Group && (request.GroupId is null || request.GroupId == Guid.Empty))
			throw Invalid("Group scope requires a nonempty GroupId.", nameof(request.Scope), nameof(request.GroupId));
		if (request.CategoryIds is null) throw Invalid("CategoryIds must be supplied explicitly.", nameof(request.CategoryIds));
		if (request.CategoryIds.Contains(Guid.Empty)) throw Invalid("Category IDs must be nonempty.", nameof(request.CategoryIds));
	}

	private static ValidationException Invalid(string message, params string[] fields) =>
		new(new ValidationResult(message, fields), null, null);
}
