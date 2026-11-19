using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.DTOs.UserLimits;
using ZPI4M.Limits.Services.Results;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.Services.UserLimits;

public interface IUserLimitService
{
	Task<LimitsResult<UserLimitResponse>> CreateAsync(Guid actor, CreateUserLimitRequest request, CancellationToken token = default);
	Task<LimitsResult<UserLimitResponse>> UpdateAsync(Guid actor, Guid limitId, UpdateUserLimitRequest request, CancellationToken token = default);
	Task<LimitsResult<UserLimitResponse>> SetEnabledAsync(Guid actor, Guid limitId, SetLimitEnabledRequest request, CancellationToken token = default);
	Task<LimitsResult<UserLimitResponse>> GetAsync(Guid actor, Guid limitId, CancellationToken token = default);
	Task<LimitsResult<PagedResult<UserLimitResponse>>> ListAsync(Guid actor, LimitListRequest query, CancellationToken token = default);
}
