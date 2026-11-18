using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;

namespace ZPI4M.Expense.Services.Aggregation;

public interface IExpenseAggregationService
{
	Task<ExpenseResult<IReadOnlyList<ExpenseCategoryBreakdownItem>>> GetCategoryBreakdownAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope,
		Guid? groupId, IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default);

	Task<ExpenseResult<IReadOnlyList<ExpensePeriodCategoryTotal>>> GetExpenseTotalsByPeriodAndCategoryAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope,
		Guid? groupId, IReadOnlyList<Guid> categoryIds, ExpenseTotalsGranularity granularity,
		CancellationToken cancellationToken = default);

	Task<ExpenseResult<IReadOnlyList<ExpenseDayOfWeekTotal>>> GetExpenseTotalsByDayOfWeekAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope,
		Guid? groupId, IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default);

	Task<ExpenseResult<IReadOnlyList<ExpenseDailyTotalsItem>>> GetDailyExpenseTotalsAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope,
		Guid? groupId, IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default);
}
