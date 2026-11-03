using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.DTOs.Invitations;

public sealed class GroupInvitationListRequest : PagedQuery
{
	public bool? IsActive { get; set; }
}
