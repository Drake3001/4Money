using ZPI4M.Grouping.DTOs.Members;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Shared.Pagination;
namespace ZPI4M.Grouping.Services.Members;

public interface IGroupMemberService
{
	Task<GroupingResult<PagedResult<GroupMemberResponse>>> ListAsync(Guid actor, Guid group, GroupMemberListRequest query, CancellationToken ct = default);
	Task<GroupingResult<GroupMemberResponse>> GetAsync(Guid actor, Guid group, Guid user, CancellationToken ct = default);
	Task<GroupingResult> LeaveAsync(Guid actor, Guid group, CancellationToken ct = default);
	Task<GroupingResult> RemoveAsync(Guid actor, Guid group, Guid user, CancellationToken ct = default);
}
