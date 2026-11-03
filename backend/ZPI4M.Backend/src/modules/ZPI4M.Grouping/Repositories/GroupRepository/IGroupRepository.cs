using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories.GroupRepository;

public interface IGroupRepository
{
	Task<Group?> LockAsync(Guid groupId, CancellationToken cancellationToken = default);
	Task<GroupBalanceSnapshot?> GetBalanceSnapshotAsync(Guid actor, Guid groupId, CancellationToken ct = default);
	Task<Group?> GetReadAsync(Guid groupId, CancellationToken ct = default);
	Task<Group?> GetForActiveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default);
	Task<PagedResult<GroupMembershipEntry>> GetMembershipPageAsync(Guid userId, GroupMembershipQuery query, CancellationToken ct = default);
	void Add(Group entity);
	void Remove(Group entity);
	Task<Group?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid groupId, CancellationToken cancellationToken = default);
	Task<PagedResult<Group>> GetPageAsync(GroupQuery query, CancellationToken cancellationToken = default);
}
