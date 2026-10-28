using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories.GroupMemberRepository;

public enum GroupMemberSortBy { UserId, IsAdmin }

public class GroupMemberQuery : PagedQuery
{
	public bool? IsAdmin { get; set; }
	public GroupMemberSortBy SortBy { get; set; } = GroupMemberSortBy.UserId;
}
