using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories;

public enum GroupSortBy { Name, MemberCount, WeeklyLimit, MonthlyLimit, YearLimit }

public class GroupQuery : PagedQuery
{
	public Guid? UserId { get; set; }
	public bool? IsAdmin { get; set; }
	public string? Name { get; set; }
	public GroupSortBy SortBy { get; set; } = GroupSortBy.Name;
}
