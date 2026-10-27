using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Services.Contracts;

public enum BankTransactionReadSortBy { Date, Amount, Status, CurrencyCode }

/// <summary>Inclusive local date/amount ranges; no status filter means all stored statuses.</summary>
public sealed class BankTransactionReadQuery : PagedQuery
{
	public Guid? BankAccountId { get; set; }
	public DateOnly? DateFrom { get; set; }
	public DateOnly? DateTo { get; set; }
	public decimal? MinAmount { get; set; }
	public decimal? MaxAmount { get; set; }
	public string? CurrencyCode { get; set; }
	public string? Description { get; set; }
	public string? Status { get; set; }
	public BankTransactionReadSortBy SortBy { get; set; } = BankTransactionReadSortBy.Date;

	public BankTransactionReadQuery() => Descending = true;
}
