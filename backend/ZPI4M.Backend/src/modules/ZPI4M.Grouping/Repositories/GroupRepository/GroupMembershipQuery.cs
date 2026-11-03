using ZPI4M.Shared.Pagination;
namespace ZPI4M.Grouping.Repositories.GroupRepository;

public sealed class GroupMembershipQuery : PagedQuery { public bool? IsMember { get; set; } }
