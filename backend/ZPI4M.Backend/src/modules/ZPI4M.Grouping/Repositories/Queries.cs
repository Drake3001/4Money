using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories;

public enum GroupSortBy { Name, MemberCount, WeeklyLimit, MonthlyLimit, YearLimit }
public enum GroupMemberSortBy { UserId, IsAdmin }

public class GroupQuery : PagedQuery
{
	public Guid? UserId { get; set; }
	public bool? IsAdmin { get; set; }
	public string? Name { get; set; }
	public GroupSortBy SortBy { get; set; } = GroupSortBy.Name;
}

public class GroupMemberQuery : PagedQuery
{
	public bool? IsAdmin { get; set; }
	public GroupMemberSortBy SortBy { get; set; } = GroupMemberSortBy.UserId;
}
