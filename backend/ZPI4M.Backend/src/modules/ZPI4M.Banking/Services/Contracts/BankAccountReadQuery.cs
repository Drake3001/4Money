using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Services.Contracts;

public enum BankAccountReadSortBy { Name, CurrencyCode, IsClosed }

/// <summary>Local account filters. Null IsClosed includes both open and closed accounts.</summary>
public sealed class BankAccountReadQuery : PagedQuery
{
	public string? Name { get; set; }
	public string? CurrencyCode { get; set; }
	public string? Type { get; set; }
	public bool? IsClosed { get; set; }
	public BankAccountReadSortBy SortBy { get; set; } = BankAccountReadSortBy.Name;
}
