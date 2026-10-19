using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories;

public enum ExpenseShareSortBy { ExpenseId, UserId, Percentage }

public class ExpenseShareQuery : PagedQuery
{
	public Guid? ExpenseId { get; set; }
	public Guid? UserId { get; set; }
	public decimal? MinPercentage { get; set; }
	public decimal? MaxPercentage { get; set; }
	public ExpenseShareSortBy SortBy { get; set; } = ExpenseShareSortBy.ExpenseId;
}
