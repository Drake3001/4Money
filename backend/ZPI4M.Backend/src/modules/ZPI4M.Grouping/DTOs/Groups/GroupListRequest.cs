using System.ComponentModel.DataAnnotations;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.DTOs.Groups;

public sealed class GroupListRequest : PagedQuery
{
	[RegularExpression("^(active|former|all)$")]
	public string Membership { get; set; } = "active";

	[Required, RegularExpression("^(name|joinedAt)$")]
	public string SortBy { get; set; } = "name";
}
