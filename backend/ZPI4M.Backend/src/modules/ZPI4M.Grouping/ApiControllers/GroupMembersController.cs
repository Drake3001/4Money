using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Grouping.DTOs.Members;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Pagination;
namespace ZPI4M.Grouping.ApiControllers;

[ApiController, Authorize, Route("api/groups/{groupId:guid}")]
[ProducesResponseType(typeof(ProblemDetails), 401)]
public sealed class GroupMembersController(ICurrentUser currentUser, IGroupMemberService service) : ControllerBase
{
	private IActionResult Read<T>(GroupingResult<T> result) => result.Status == 200 ? Ok(result.Value) : Problem(statusCode: result.Status);
	private IActionResult Changed(GroupingResult result) => result.Status == 204 ? NoContent() : Problem(statusCode: result.Status);
	[HttpGet("members"), ProducesResponseType(typeof(PagedResult<GroupMemberResponse>), 200)]
	public async Task<IActionResult> List(Guid groupId, [FromQuery] GroupMemberListRequest query, CancellationToken ct) => currentUser.UserId is Guid actor ? Read(await service.ListAsync(actor, groupId, query, ct)) : Unauthorized();
	[HttpGet("members/{userId:guid}"), ProducesResponseType(typeof(GroupMemberResponse), 200)]
	public async Task<IActionResult> Get(Guid groupId, Guid userId, CancellationToken ct) => currentUser.UserId is Guid actor ? Read(await service.GetAsync(actor, groupId, userId, ct)) : Unauthorized();
	[HttpPost("leave"), ProducesResponseType(204)]
	public async Task<IActionResult> Leave(Guid groupId, CancellationToken ct) => currentUser.UserId is Guid actor ? Changed(await service.LeaveAsync(actor, groupId, ct)) : Unauthorized();
	[HttpDelete("members/{userId:guid}"), ProducesResponseType(204)]
	public async Task<IActionResult> Remove(Guid groupId, Guid userId, CancellationToken ct) => currentUser.UserId is Guid actor ? Changed(await service.RemoveAsync(actor, groupId, userId, ct)) : Unauthorized();
}
