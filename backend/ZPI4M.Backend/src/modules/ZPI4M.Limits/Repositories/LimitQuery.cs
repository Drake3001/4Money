using ZPI4M.Limits.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.Repositories;

public sealed class LimitQuery : PagedQuery
{
	public bool IncludeDisabled { get; init; }
	public Guid? CategoryId { get; init; }
	public string? CurrencyCode { get; init; }
	public LimitPeriod? Period { get; init; }
}
