using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Grouping.Services.ExpenseMetadata;

namespace ZPI4M.Expense.Services.Aggregation;

public sealed class ExpenseAggregationService(IExpenseRepository expenses,
	IGroupExpenseMetadataLookup groups) : IExpenseAggregationService
{
	public Task<ExpenseResult<IReadOnlyList<ExpenseCategoryBreakdownItem>>> GetCategoryBreakdownAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId,
		IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default) =>
		ReadAsync(actor, dateFrom, dateTo, scope, groupId, categoryIds, ExpenseAggregationType.Category,
			row => new ExpenseCategoryBreakdownItem(row.CategoryId, row.TotalAmount), cancellationToken);

	public Task<ExpenseResult<IReadOnlyList<ExpensePeriodCategoryTotal>>> GetExpenseTotalsByPeriodAndCategoryAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId,
		IReadOnlyList<Guid> categoryIds, ExpenseTotalsGranularity granularity, CancellationToken cancellationToken = default)
	{
		if (!Enum.IsDefined(granularity))
			return Task.FromResult(ExpenseResult<IReadOnlyList<ExpensePeriodCategoryTotal>>.Failure(ExpenseErrorCode.InvalidExpense,
				[new("Unknown granularity.", [nameof(granularity)])]));
		var aggregation = granularity switch
		{
			ExpenseTotalsGranularity.Month => ExpenseAggregationType.Month,
			ExpenseTotalsGranularity.Quarter => ExpenseAggregationType.Quarter,
			_ => ExpenseAggregationType.Year
		};
		return ReadAsync(actor, dateFrom, dateTo, scope, groupId, categoryIds, aggregation,
			row => new ExpensePeriodCategoryTotal(row.Period, row.CategoryId, row.TotalAmount), cancellationToken);
	}

	public async Task<ExpenseResult<IReadOnlyList<ExpenseDayOfWeekTotal>>> GetExpenseTotalsByDayOfWeekAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId,
		IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default)
	{
		var result = await ReadAsync(actor, dateFrom, dateTo, scope, groupId, categoryIds, ExpenseAggregationType.DayOfWeek,
			row => new ExpenseDayOfWeekTotal((DayOfWeek)row.Weekday, row.TotalAmount), cancellationToken);
		// Monday through Sunday, retaining the System.DayOfWeek contract.
		return result.IsSuccess
			? ExpenseResult<IReadOnlyList<ExpenseDayOfWeekTotal>>.Success(result.Value!.OrderBy(row => ((int)row.DayOfWeek + 6) % 7).ToArray())
			: result;
	}

	public Task<ExpenseResult<IReadOnlyList<ExpenseDailyTotalsItem>>> GetDailyExpenseTotalsAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId,
		IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default) =>
		ReadAsync(actor, dateFrom, dateTo, scope, groupId, categoryIds, ExpenseAggregationType.Day,
			row => new ExpenseDailyTotalsItem(row.Period, row.TotalAmount), cancellationToken);

	private async Task<ExpenseResult<IReadOnlyList<T>>> ReadAsync<T>(Guid actor, DateOnly dateFrom, DateOnly dateTo,
		ExpenseAggregationScope scope, Guid? groupId, IReadOnlyList<Guid> categoryIds, ExpenseAggregationType aggregation,
		Func<ExpenseAggregateRow, T> project, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		if (actor == Guid.Empty) return ExpenseResult<IReadOnlyList<T>>.Failure(ExpenseErrorCode.Forbidden);
		// The caller owns request validation. Fail closed if an internal caller violates the contract.
		if (!Enum.IsDefined(scope) || dateFrom > dateTo || categoryIds is null || categoryIds.Contains(Guid.Empty) ||
			(scope == ExpenseAggregationScope.Personal ? groupId is not null : groupId is null || groupId == Guid.Empty))
			return ExpenseResult<IReadOnlyList<T>>.Failure(ExpenseErrorCode.InvalidExpense);
		var active = await groups.GetActiveGroupIdsAsync(actor, token);
		if (groupId is Guid id && !active.Contains(id) && !await expenses.HasRelatedExpenseInGroupAsync(actor, id, token))
			return ExpenseResult<IReadOnlyList<T>>.Failure(ExpenseErrorCode.NotFound);
		if (categoryIds.Count == 0) return ExpenseResult<IReadOnlyList<T>>.Success([]);
		var rows = await expenses.GetAggregatedExpenseTotalsAsync(actor, active, dateFrom, dateTo, scope, groupId, categoryIds, aggregation, token);
		return ExpenseResult<IReadOnlyList<T>>.Success(rows.Select(project).ToArray());
	}
}
