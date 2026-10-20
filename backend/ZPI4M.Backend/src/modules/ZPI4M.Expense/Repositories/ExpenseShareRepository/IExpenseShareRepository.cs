using ZPI4M.Expense.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseShareRepository;

public interface IExpenseShareRepository
{
	void Add(ExpenseShare entity);
	void Remove(ExpenseShare entity);
	Task<ExpenseShare?> GetAsync(Guid expenseId, Guid userId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid expenseId, Guid userId, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<ExpenseShare>> GetByExpenseAsync(Guid expenseId, CancellationToken cancellationToken = default);
	Task<decimal> GetTotalPercentageAsync(Guid expenseId, CancellationToken cancellationToken = default);
	Task<PagedResult<ExpenseShare>> GetPageAsync(ExpenseShareQuery query, CancellationToken cancellationToken = default);
}
