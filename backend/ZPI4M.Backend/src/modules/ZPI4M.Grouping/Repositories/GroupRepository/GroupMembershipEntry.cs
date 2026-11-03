using ZPI4M.Grouping.Models;
namespace ZPI4M.Grouping.Repositories.GroupRepository;

public sealed record GroupMembershipEntry(Group Group, bool IsMember);
