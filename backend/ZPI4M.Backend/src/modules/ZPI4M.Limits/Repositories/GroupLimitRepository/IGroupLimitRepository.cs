using ZPI4M.Limits.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.Repositories.GroupLimitRepository;

public interface IGroupLimitRepository
{
	Task<GroupLimit?> GetAsync(Guid groupId, Guid limitId, bool tracking, CancellationToken token = default);
	Task<PagedResult<GroupLimit>> GetPageAsync(Guid groupId, LimitQuery query, CancellationToken token = default);
	Task<GroupLimit[]> GetEnabledScopeAsync(Guid groupId, Guid? categoryId, string currency, CancellationToken token = default);
	void Add(GroupLimit value);
}
