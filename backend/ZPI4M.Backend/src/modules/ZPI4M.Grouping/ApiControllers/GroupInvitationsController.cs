using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Grouping.DTOs.Invitations;
using ZPI4M.Grouping.Services.Invitations;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Shared.CurrentUser;
namespace ZPI4M.Grouping.ApiControllers;

[ApiController, Authorize, Route("api/groups/{groupId:guid}/invitations")]
[TypeFilter(typeof(GroupIconExceptionFilter))]
public sealed class GroupInvitationsController(ICurrentUser currentUser, IGroupInvitationService service) : ControllerBase
{
	private async Task<IActionResult> Run<T>(Func<Guid, Task<GroupingResult<T>>> action)
	{
		Response.Headers.CacheControl = "no-store";
		if (currentUser.UserId is not Guid user) return Unauthorized();
		var r = await action(user);
		return r.Status == 204 ? NoContent() : r.Status == 400 ? ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["request"] = ["Invalid invitation data."] })) : r.Value is not null ? StatusCode(r.Status, r.Value) : Problem(statusCode: r.Status);
	}
	private async Task<IActionResult> RunMutation(Func<Guid, Task<GroupingResult>> action)
	{
		Response.Headers.CacheControl = "no-store";
		if (currentUser.UserId is not Guid user) return Unauthorized();
		var result = await action(user); return result.Status == 204 ? NoContent() : Problem(statusCode: result.Status);
	}

	[ProducesResponseType(typeof(ZPI4M.Shared.Pagination.PagedResult<GroupInvitationResponse>), 200)]
	[HttpGet] public Task<IActionResult> List(Guid groupId, [FromQuery] GroupInvitationListRequest query, CancellationToken ct) => Run(u => service.ListAsync(u, groupId, query, ct));
	[ProducesResponseType(typeof(CreateGroupInvitationResponse), 201)]
	[HttpPost] public Task<IActionResult> Create(Guid groupId, CreateGroupInvitationRequest request, CancellationToken ct) => Run(u => service.CreateAsync(u, groupId, request, ct));
	[HttpDelete("{invitationId:guid}")] public Task<IActionResult> Revoke(Guid groupId, Guid invitationId, CancellationToken ct) => RunMutation(u => service.RevokeAsync(u, groupId, invitationId, ct));
	[HttpPost("/api/group-invitations/preview")] public Task<IActionResult> Preview(AcceptGroupInvitationRequest request, CancellationToken ct) => Run(u => service.PreviewAsync(request.Token!, ct));
	[HttpPost("/api/group-invitations/accept")] public Task<IActionResult> Accept(AcceptGroupInvitationRequest request, CancellationToken ct) => Run(u => service.AcceptAsync(u, request.Token!, ct));
}
