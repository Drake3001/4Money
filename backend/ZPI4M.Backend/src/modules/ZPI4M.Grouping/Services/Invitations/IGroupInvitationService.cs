using ZPI4M.Grouping.DTOs.Invitations;
using ZPI4M.Grouping.DTOs.Members;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Services.Invitations;

public interface IGroupInvitationService
{
	Task<GroupingResult<CreateGroupInvitationResponse>> CreateAsync(Guid user, Guid group, CreateGroupInvitationRequest request, CancellationToken ct = default);
	Task<GroupingResult<PagedResult<GroupInvitationResponse>>> ListAsync(Guid user, Guid group, GroupInvitationListRequest query, CancellationToken ct = default);
	Task<GroupingResult> RevokeAsync(Guid user, Guid group, Guid invitation, CancellationToken ct = default);
	Task<GroupingResult<InvitationPreview>> PreviewAsync(string token, CancellationToken ct = default);
	Task<GroupingResult<GroupMemberResponse>> AcceptAsync(Guid user, string token, CancellationToken ct = default);
}
