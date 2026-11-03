using System.ComponentModel.DataAnnotations;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.DTOs.Members;

public sealed class GroupMemberListRequest : PagedQuery
{
	[RegularExpression("^(active|former|all)$")]
	public string Membership { get; set; } = "active";
}
