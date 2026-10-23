using ZPI4M.Expense.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseItemRepository;

public interface IExpenseItemRepository
{
	void Add(ExpenseItem entity);
	void AddRange(IEnumerable<ExpenseItem> items);
	void Remove(ExpenseItem entity);
	Task<ExpenseItem?> GetByIdAsync(Guid expenseItemId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid expenseItemId, CancellationToken cancellationToken = default);
	Task<PagedResult<ExpenseItem>> GetByExpenseAsync(Guid expenseId, ExpenseItemQuery query, CancellationToken cancellationToken = default);
}
