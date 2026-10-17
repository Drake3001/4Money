using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Repositories;

public enum BankAccountSortBy { Name, Number, FinancialInstitutionId, CurrencyCode, IsClosed }
public enum BankTransactionSortBy { Date, Amount, Status, CurrencyCode }

public class BankAccountQuery : PagedQuery
{
	public Guid? UserId { get; set; }
	public string? Name { get; set; }
	public string? Number { get; set; }
	public string? Iban { get; set; }
	public string? FinancialInstitutionId { get; set; }
	public string? CurrencyCode { get; set; }
	public string? Type { get; set; }
	public bool? IsClosed { get; set; }
	public BankAccountSortBy SortBy { get; set; } = BankAccountSortBy.Name;
}

public class BankTransactionQuery : PagedQuery
{
	public Guid? BankAccountId { get; set; }
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
