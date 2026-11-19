using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.DTOs.UserLimits;
using ZPI4M.Limits.Services.Results;
using ZPI4M.Limits.Services.UserLimits;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.ApiControllers;

[ApiController, Authorize, Route("api/users/me/limits")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ValidationProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 403)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
[ProducesResponseType(typeof(ProblemDetails), 409)]
public sealed class UserLimitsController(ICurrentUser currentUser, IUserLimitService limits) : ControllerBase
{
	[HttpGet, ProducesResponseType(typeof(PagedResult<UserLimitResponse>), 200)]
	public async Task<IActionResult> List([FromQuery] LimitListRequest query, CancellationToken token) => currentUser.UserId is Guid actor ? Result(await limits.ListAsync(actor, query, token)) : Unauthorized();
	[HttpGet("{limitId:guid}"), ProducesResponseType(typeof(UserLimitResponse), 200), ProducesResponseType(typeof(ProblemDetails), 503)]
	public async Task<IActionResult> Get(Guid limitId, CancellationToken token) => currentUser.UserId is Guid actor ? Result(await limits.GetAsync(actor, limitId, token)) : Unauthorized();
	[HttpPost, Consumes("application/json"), ProducesResponseType(typeof(UserLimitResponse), 201)]
	public async Task<IActionResult> Create([FromBody] CreateUserLimitRequest request, CancellationToken token)
	{
		if (currentUser.UserId is not Guid actor)
			return Unauthorized();
		var result = await limits.CreateAsync(actor, request, token);
		return result.Status == 201 ? CreatedAtAction(nameof(Get), new { limitId = result.Value!.Id }, result.Value) : Result(result);
	}

	[HttpPut("{limitId:guid}"), Consumes("application/json"), ProducesResponseType(typeof(UserLimitResponse), 200)]
	public async Task<IActionResult> Update(Guid limitId, [FromBody] UpdateUserLimitRequest request, CancellationToken token) => currentUser.UserId is Guid actor ? Result(await limits.UpdateAsync(actor, limitId, request, token)) : Unauthorized();
	[HttpPatch("{limitId:guid}/enabled"), Consumes("application/json"), ProducesResponseType(typeof(UserLimitResponse), 200)]
	public async Task<IActionResult> SetEnabled(Guid limitId, [FromBody] SetLimitEnabledRequest request, CancellationToken token) => currentUser.UserId is Guid actor ? Result(await limits.SetEnabledAsync(actor, limitId, request, token)) : Unauthorized();
	private IActionResult Result<T>(LimitsResult<T> result) => result.Status switch
	{
		400 => ValidationProblem(new ValidationProblemDetails(result.Errors?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, string[]> { ["request"] = ["Invalid limit data."] })),
		>= 200 and < 300 => StatusCode(result.Status, result.Value),
		_ => Problem(statusCode: result.Status, extensions: result.Code is null ? null : new Dictionary<string, object?> { ["code"] = result.Code })
	};
}
