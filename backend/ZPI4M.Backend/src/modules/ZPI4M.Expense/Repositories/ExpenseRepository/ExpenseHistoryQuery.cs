using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseRepository;

public sealed class ExpenseHistoryQuery : PagedQuery
{
	public Guid? GroupId { get; init; }
	public Guid? ParticipantId { get; init; }
	public Guid? PayerId { get; init; }
	public DateOnly? TransactionTimeFrom { get; init; }
	public DateOnly? TransactionTimeTo { get; init; }
	public decimal? MinAmount { get; init; }
	public decimal? MaxAmount { get; init; }
	public Guid? CategoryId { get; init; }
	public bool? HasReceipt { get; init; }
	public bool? HasBankTransaction { get; init; }
	public string? Search { get; init; }
	public ExpenseSortBy SortBy { get; init; }
}
