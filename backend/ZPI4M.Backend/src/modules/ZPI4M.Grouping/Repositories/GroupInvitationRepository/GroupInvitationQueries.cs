using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories.GroupInvitationRepository;

public sealed class GroupInvitationQuery : PagedQuery
{
	public bool? IsActive { get; set; }
}
