using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseItemRepository;

public enum ExpenseItemSortBy { Name, UnitPrice, Quantity }

public class ExpenseItemQuery : PagedQuery
{
	public string? Name { get; set; }
	public Guid? CategoryId { get; set; }
	public Guid? UserId { get; set; }
	public ExpenseItemSortBy SortBy { get; set; } = ExpenseItemSortBy.Name;
}
