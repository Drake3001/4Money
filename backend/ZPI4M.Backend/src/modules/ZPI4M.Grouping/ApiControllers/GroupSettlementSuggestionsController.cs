using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Grouping.DTOs.Suggestions;
using ZPI4M.Grouping.Services.Suggestions;
using ZPI4M.Shared.CurrentUser;
namespace ZPI4M.Grouping.ApiControllers;

[ApiController, Authorize, Route("api/groups/{groupId:guid}/settlements/suggestions")]
public sealed class GroupSettlementSuggestionsController(ICurrentUser user, IGroupSettlementSuggestionService service) : ControllerBase
{
	[HttpGet, ProducesResponseType(typeof(GroupSettlementSuggestionsResponse), 200)]
	[ProducesResponseType(typeof(ProblemDetails), 401)]
	[ProducesResponseType(typeof(ProblemDetails), 404)]
	[ProducesResponseType(typeof(ProblemDetails), 409)]
	public async Task<IActionResult> Get(Guid groupId, CancellationToken ct)
	{
		Response.Headers.CacheControl = "no-store";
		if (user.UserId is not Guid actor) return Unauthorized();
		var result = await service.GetAsync(actor, groupId, ct);
		return result.Status == 200 ? Ok(result.Value) : Problem(statusCode: result.Status, title: result.Status == 409 ? "Group balances are inconsistent or exceed settlement limits." : null);
	}
}
