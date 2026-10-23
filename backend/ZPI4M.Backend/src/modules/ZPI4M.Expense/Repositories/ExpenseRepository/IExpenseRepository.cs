using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseRepository;

public interface IExpenseRepository
{
	void Add(Models.Expense entity);
	void Remove(Models.Expense entity);
	Task<Models.Expense?> GetByIdAsync(Guid expenseId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid expenseId, CancellationToken cancellationToken = default);
	Task<PagedResult<Models.Expense>> GetByGroupAsync(Guid groupId, ExpenseQuery query, CancellationToken cancellationToken = default);
}
