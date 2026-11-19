using ZPI4M.Limits.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.Repositories.UserLimitRepository;

public interface IUserLimitRepository
{
	Task<UserLimit?> GetAsync(Guid userId, Guid limitId, bool tracking, CancellationToken token = default);
	Task<PagedResult<UserLimit>> GetPageAsync(Guid userId, LimitQuery query, CancellationToken token = default);
	Task<UserLimit[]> GetEnabledScopeAsync(Guid userId, Guid? categoryId, string currency, CancellationToken token = default);
	void Add(UserLimit value);
}
