using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseRepository;

public enum ExpenseSortBy { TransactionTime, Name, Amount }

public class ExpenseQuery : PagedQuery
{
	public ExpenseQuery()
	{
		Descending = true;
	}

	public string? Name { get; set; }
	public string? Merchant { get; set; }
	public DateOnly? DateFrom { get; set; }
	public DateOnly? DateTo { get; set; }
	public decimal? MinAmount { get; set; }
	public decimal? MaxAmount { get; set; }
	public ExpenseSortBy SortBy { get; set; } = ExpenseSortBy.TransactionTime;
}
