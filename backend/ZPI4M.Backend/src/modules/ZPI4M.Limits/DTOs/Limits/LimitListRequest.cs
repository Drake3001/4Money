using ZPI4M.Limits.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.DTOs.Limits;

public sealed class LimitListRequest : PagedQuery
{
	public bool IncludeDisabled { get; set; }
	public Guid? CategoryId { get; set; }
	public string? CurrencyCode { get; set; }
	public LimitPeriod? Period { get; set; }
}
