using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories.GroupRepository;

public enum GroupSortBy { Name, MemberCount }

public class GroupQuery : PagedQuery
{
	public Guid? UserId { get; set; }
	public bool? IsAdmin { get; set; }
	public string? Name { get; set; }
	public GroupSortBy SortBy { get; set; } = GroupSortBy.Name;
}
