using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Grouping.DTOs.Settlements;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Grouping.Services.Settlements;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Pagination;
namespace ZPI4M.Grouping.ApiControllers;

[ApiController, Authorize, Route("api/groups/{groupId:guid}/settlements")]
public sealed class GroupSettlementsController(ICurrentUser currentUser, IGroupSettlementService service) : ControllerBase
{
	private IActionResult Result<T>(GroupingResult<T> r) => r.Status == 204 ? NoContent() : r.Status == 400 ? ValidationProblem(new ValidationProblemDetails(r.Errors is null ? new Dictionary<string, string[]> { ["request"] = ["Invalid settlement data."] } : r.Errors.ToDictionary(e => e.Key, e => e.Value))) : r.Status is >= 200 and < 300 ? StatusCode(r.Status, r.Value) : Problem(statusCode: r.Status);
	private IActionResult Mutation(GroupingResult r) => r.Status == 204 ? NoContent() : Problem(statusCode: r.Status);

	[HttpPost, ProducesResponseType(typeof(GroupSettlementResponse), 201)]
	public async Task<IActionResult> Create(Guid groupId, CreateGroupSettlementRequest request, CancellationToken ct)
	{
		if (currentUser.UserId is not Guid actor) return Unauthorized();
		var r = await service.CreateAsync(actor, groupId, request, ct);
		return r.Status == 201 ? CreatedAtAction(nameof(Get), new { groupId, settlementId = r.Value!.Id }, r.Value) : Result(r);
	}
	[HttpGet, ProducesResponseType(typeof(PagedResult<GroupSettlementResponse>), 200)]
	public async Task<IActionResult> List(Guid groupId, [FromQuery] GroupSettlementListRequest query, CancellationToken ct) => currentUser.UserId is Guid actor ? Result(await service.ListAsync(actor, groupId, query, ct)) : Unauthorized();
	[HttpGet("{settlementId:guid}"), ProducesResponseType(typeof(GroupSettlementResponse), 200)]
	public async Task<IActionResult> Get(Guid groupId, Guid settlementId, CancellationToken ct) => currentUser.UserId is Guid actor ? Result(await service.GetAsync(actor, groupId, settlementId, ct)) : Unauthorized();
	[HttpPut("{settlementId:guid}"), ProducesResponseType(typeof(GroupSettlementResponse), 200)]
	public async Task<IActionResult> Update(Guid groupId, Guid settlementId, UpdateGroupSettlementRequest request, CancellationToken ct) => currentUser.UserId is Guid actor ? Result(await service.UpdateAsync(actor, groupId, settlementId, request, ct)) : Unauthorized();
	[HttpDelete("{settlementId:guid}"), ProducesResponseType(204)]
	public async Task<IActionResult> Remove(Guid groupId, Guid settlementId, CancellationToken ct) => currentUser.UserId is Guid actor ? Mutation(await service.DeleteAsync(actor, groupId, settlementId, ct)) : Unauthorized();
}
