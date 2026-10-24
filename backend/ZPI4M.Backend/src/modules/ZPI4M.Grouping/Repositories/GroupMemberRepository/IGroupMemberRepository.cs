using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories.GroupMemberRepository;

public interface IGroupMemberRepository
{
	void Add(GroupMember entity);
	void Remove(GroupMember entity);
	Task<GroupMember?> GetAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
	Task<bool> IsMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
	Task<bool> IsAdminAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
	Task<int> CountAsync(Guid groupId, bool? isAdmin = null, CancellationToken cancellationToken = default);
	Task<PagedResult<GroupMember>> GetPageAsync(Guid groupId, GroupMemberQuery query, CancellationToken cancellationToken = default);
}
