using ZPI4M.Grouping.DTOs.Settlements;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Shared.Pagination;
namespace ZPI4M.Grouping.Services.Settlements;

public interface IGroupSettlementService
{
	Task<GroupingResult<GroupSettlementResponse>> CreateAsync(Guid actor, Guid group, CreateGroupSettlementRequest request, CancellationToken ct = default);
	Task<GroupingResult<GroupSettlementResponse>> UpdateAsync(Guid actor, Guid group, Guid id, UpdateGroupSettlementRequest request, CancellationToken ct = default);
	Task<GroupingResult> DeleteAsync(Guid actor, Guid group, Guid id, CancellationToken ct = default);
	Task<GroupingResult<GroupSettlementResponse>> GetAsync(Guid actor, Guid group, Guid id, CancellationToken ct = default);
	Task<GroupingResult<PagedResult<GroupSettlementResponse>>> ListAsync(Guid actor, Guid group, GroupSettlementListRequest query, CancellationToken ct = default);
}
