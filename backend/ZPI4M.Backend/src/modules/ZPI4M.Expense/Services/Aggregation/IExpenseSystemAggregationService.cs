using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;

namespace ZPI4M.Expense.Services.Aggregation;

/// <summary>Trusted callers only: establish and lock the active PLN group in the coordinator before reading.</summary>
public interface IExpenseSystemAggregationService
{
	Task<ExpenseResult<ExpenseUsageTotal>> GetGroupUsageTotalAsync(Guid groupId, DateOnly dateFrom,
		DateOnly dateTo, Guid? categoryId, CancellationToken cancellationToken = default);
}
