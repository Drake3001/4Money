using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseShareRepository;

public enum ExpenseShareSortBy { ExpenseId, UserId, Amount }

public class ExpenseShareQuery : PagedQuery
{
	public Guid? ExpenseId { get; set; }
	public Guid? UserId { get; set; }
	public decimal? MinAmount { get; set; }
	public decimal? MaxAmount { get; set; }
	public ExpenseShareSortBy SortBy { get; set; } = ExpenseShareSortBy.ExpenseId;
}
