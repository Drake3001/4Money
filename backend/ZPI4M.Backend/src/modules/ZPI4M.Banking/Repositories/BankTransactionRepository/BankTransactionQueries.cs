using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Repositories.BankTransactionRepository;

public enum BankTransactionSortBy { Date, Amount, Status, CurrencyCode }

public class BankTransactionQuery : PagedQuery
{
	public Guid? BankAccountId { get; set; }
	public bool? SettlementAssignment { get; set; }
	public bool? ExpenseAssignment { get; set; }
	public Guid? UserId { get; set; }
	public string? Status { get; set; }
	public string? Description { get; set; }
	public string? CurrencyCode { get; set; }
	public DateOnly? DateFrom { get; set; }
	public DateOnly? DateTo { get; set; }
	public decimal? MinAmount { get; set; }
	public decimal? MaxAmount { get; set; }
	public BankTransactionSortBy SortBy { get; set; } = BankTransactionSortBy.Date;
}
