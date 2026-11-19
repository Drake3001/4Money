using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseRepository;

public interface IExpenseRepository
{
	Task<decimal> GetUsageTotalAsync(Guid actor, IReadOnlyList<Guid> activeGroupIds, DateOnly dateFrom,
		DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, Guid? categoryId,
		CancellationToken cancellationToken = default);

	Task<bool> HasRelatedExpenseInGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);
	Task<PagedResult<Models.Expense>> GetHistoryAsync(Guid actor, IReadOnlyList<Guid> activeGroupIds, ExpenseHistoryQuery query, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<ExpenseAggregateRow>> GetAggregatedExpenseTotalsAsync(Guid actor, IReadOnlyList<Guid> activeGroupIds,
		DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId,
		IReadOnlyList<Guid> categoryIds, ExpenseAggregationType aggregation,
		CancellationToken cancellationToken = default);
	void Add(Models.Expense entity);
	void Remove(Models.Expense entity);
	Task<Models.Expense?> GetByIdAsync(Guid expenseId, CancellationToken cancellationToken = default);
	Task<Models.Expense?> GetReadAsync(Guid expenseId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid expenseId, CancellationToken cancellationToken = default);
}
