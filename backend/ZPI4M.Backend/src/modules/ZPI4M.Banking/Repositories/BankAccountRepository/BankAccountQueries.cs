using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Repositories.BankAccountRepository;

public enum BankAccountSortBy { Name, Number, FinancialInstitutionId, CurrencyCode, IsClosed }

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
