using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories.GroupMemberRepository;

public interface IGroupMemberRepository
{
	Task<GroupMember?> GetReadAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
	Task<Dictionary<Guid, GroupMember>> GetManyAsync(Guid groupId, IReadOnlyCollection<Guid> users, CancellationToken ct = default);
	Task<IReadOnlyList<Guid>> GetActiveUserIdsAsync(Guid groupId, Guid? excludedUserId = null, CancellationToken ct = default);
	void Add(GroupMember entity);
	void Remove(GroupMember entity);
	Task<GroupMember?> GetAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
	Task<bool> IsMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
	Task<bool> IsAdminAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
	Task<int> CountAsync(Guid groupId, bool? isAdmin = null, CancellationToken cancellationToken = default);
	Task<PagedResult<GroupMember>> GetPageAsync(Guid groupId, GroupMemberQuery query, CancellationToken cancellationToken = default);
}
