using ZPI4M.Limits.DTOs.GroupLimits;
using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.Services.Results;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.Services.GroupLimits;

public interface IGroupLimitService
{
	Task<LimitsResult<GroupLimitResponse>> CreateAsync(Guid actor, Guid groupId, CreateGroupLimitRequest request, CancellationToken token = default);
	Task<LimitsResult<GroupLimitResponse>> UpdateAsync(Guid actor, Guid groupId, Guid limitId, UpdateGroupLimitRequest request, CancellationToken token = default);
	Task<LimitsResult<GroupLimitResponse>> SetEnabledAsync(Guid actor, Guid groupId, Guid limitId, SetLimitEnabledRequest request, CancellationToken token = default);
	Task<LimitsResult<GroupLimitResponse>> GetAsync(Guid actor, Guid groupId, Guid limitId, CancellationToken token = default);
	Task<LimitsResult<PagedResult<GroupLimitResponse>>> ListAsync(Guid actor, Guid groupId, LimitListRequest query, CancellationToken token = default);
}
