using ZPI4M.Grouping.DTOs.Groups;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Shared.Pagination;
namespace ZPI4M.Grouping.Services.Groups;

public interface IGroupService
{
	Task<GroupingResult> ArchiveAsync(Guid userId, Guid groupId, CancellationToken ct = default);
	Task<GroupingResult<GroupResponse>> CreateAsync(Guid userId, CreateGroupRequest request, CancellationToken ct = default);
	Task<GroupingResult<PagedResult<GroupResponse>>> GetPageAsync(Guid userId, GroupListRequest query, CancellationToken ct = default);
	Task<GroupingResult<GroupResponse>> GetAsync(Guid userId, Guid groupId, CancellationToken ct = default);
	Task<GroupingResult<GroupResponse>> UpdateAsync(Guid userId, Guid groupId, UpdateGroupRequest request, CancellationToken ct = default);
	Task<GroupingResult<GroupResponse>> TransferAdministratorAsync(Guid userId, Guid groupId, Guid targetId, CancellationToken ct = default);
}
