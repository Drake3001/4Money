using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories.GroupRepository;

public interface IGroupRepository
{
	void Add(Group entity);
	void Remove(Group entity);
	Task<Group?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid groupId, CancellationToken cancellationToken = default);
	Task<PagedResult<Group>> GetPageAsync(GroupQuery query, CancellationToken cancellationToken = default);
}
