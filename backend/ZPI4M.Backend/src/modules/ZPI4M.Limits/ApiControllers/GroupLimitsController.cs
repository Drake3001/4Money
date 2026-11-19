using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Limits.DTOs.GroupLimits;
using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.Services.GroupLimits;
using ZPI4M.Limits.Services.Results;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.ApiControllers;

[ApiController, Authorize, Route("api/groups/{groupId:guid}/limits")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ValidationProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 403)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
[ProducesResponseType(typeof(ProblemDetails), 409)]
public sealed class GroupLimitsController(ICurrentUser currentUser, IGroupLimitService limits) : ControllerBase
{
	[HttpGet, ProducesResponseType(typeof(PagedResult<GroupLimitResponse>), 200)]
	public async Task<IActionResult> List(Guid groupId, [FromQuery] LimitListRequest query, CancellationToken token) => currentUser.UserId is Guid actor ? Result(await limits.ListAsync(actor, groupId, query, token)) : Unauthorized();
	[HttpGet("{limitId:guid}"), ProducesResponseType(typeof(GroupLimitResponse), 200), ProducesResponseType(typeof(ProblemDetails), 503)]
	public async Task<IActionResult> Get(Guid groupId, Guid limitId, CancellationToken token) => currentUser.UserId is Guid actor ? Result(await limits.GetAsync(actor, groupId, limitId, token)) : Unauthorized();
	[HttpPost, Consumes("application/json"), ProducesResponseType(typeof(GroupLimitResponse), 201)]
	public async Task<IActionResult> Create(Guid groupId, [FromBody] CreateGroupLimitRequest request, CancellationToken token)
	{
		if (currentUser.UserId is not Guid actor)
			return Unauthorized();
		var result = await limits.CreateAsync(actor, groupId, request, token);
		return result.Status == 201 ? CreatedAtAction(nameof(Get), new { groupId, limitId = result.Value!.Id }, result.Value) : Result(result);
	}

	[HttpPut("{limitId:guid}"), Consumes("application/json"), ProducesResponseType(typeof(GroupLimitResponse), 200)]
	public async Task<IActionResult> Update(Guid groupId, Guid limitId, [FromBody] UpdateGroupLimitRequest request, CancellationToken token) => currentUser.UserId is Guid actor ? Result(await limits.UpdateAsync(actor, groupId, limitId, request, token)) : Unauthorized();
	[HttpPatch("{limitId:guid}/enabled"), Consumes("application/json"), ProducesResponseType(typeof(GroupLimitResponse), 200)]
	public async Task<IActionResult> SetEnabled(Guid groupId, Guid limitId, [FromBody] SetLimitEnabledRequest request, CancellationToken token) => currentUser.UserId is Guid actor ? Result(await limits.SetEnabledAsync(actor, groupId, limitId, request, token)) : Unauthorized();
	private IActionResult Result<T>(LimitsResult<T> result) => result.Status switch
	{
		400 => ValidationProblem(new ValidationProblemDetails(result.Errors?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, string[]> { ["request"] = ["Invalid limit data."] })),
		>= 200 and < 300 => StatusCode(result.Status, result.Value),
		_ => Problem(statusCode: result.Status, extensions: result.Code is null ? null : new Dictionary<string, object?> { ["code"] = result.Code })
	};
}
