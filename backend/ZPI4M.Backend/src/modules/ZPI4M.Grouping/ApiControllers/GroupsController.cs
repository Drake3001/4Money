using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Grouping.DTOs.Groups;
using ZPI4M.Grouping.Services.Groups;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Shared.CurrentUser;
namespace ZPI4M.Grouping.ApiControllers;

[ApiController, Authorize, Route("api/groups"), TypeFilter(typeof(GroupIconExceptionFilter))]
public sealed class GroupsController(ICurrentUser currentUser, IGroupService groups, IGroupIconService icons) : ControllerBase
{
	private IActionResult Result<T>(GroupingResult<T> result) => result.Status == 400 ? ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["request"] = ["Invalid group data or icon selection."] })) : result.Status == 204 ? NoContent() : result.Value is not null ? StatusCode(result.Status, result.Value) : Problem(statusCode: result.Status);
	private IActionResult Mutation(GroupingResult result) => result.Status == 204 ? NoContent() : Problem(statusCode: result.Status);

	[HttpDelete("{groupId:guid}")]
	public async Task<IActionResult> Delete(Guid groupId, CancellationToken ct) =>
	 currentUser.UserId is Guid id ? Mutation(await groups.ArchiveAsync(id, groupId, ct)) : Unauthorized();

	[HttpPost, Consumes("application/json")]
	public async Task<IActionResult> Create(CreateGroupRequest request, CancellationToken ct)
	{
		if (currentUser.UserId is not Guid id) return Unauthorized();
		var result = await groups.CreateAsync(id, request, ct);
		return result.Status == 201 ? CreatedAtAction(nameof(Get), new { groupId = result.Value!.Id }, result.Value) : Result(result);
	}
	[HttpGet]
	public async Task<IActionResult> List([FromQuery] GroupListRequest query, CancellationToken ct) =>
	 currentUser.UserId is Guid id ? Result(await groups.GetPageAsync(id, query, ct)) : Unauthorized();
	[HttpGet("{groupId:guid}")]
	public async Task<IActionResult> Get(Guid groupId, CancellationToken ct) =>
	 currentUser.UserId is Guid id ? Result(await groups.GetAsync(id, groupId, ct)) : Unauthorized();
	[HttpPut("{groupId:guid}"), Consumes("application/json")]
	public async Task<IActionResult> Update(Guid groupId, UpdateGroupRequest request, CancellationToken ct) =>
	 currentUser.UserId is Guid id ? Result(await groups.UpdateAsync(id, groupId, request, ct)) : Unauthorized();
	[HttpPut("{groupId:guid}/administrator")]
	public async Task<IActionResult> TransferAdministrator(Guid groupId, TransferGroupAdministratorRequest request, CancellationToken ct) =>
	 currentUser.UserId is Guid id ? Result(await groups.TransferAdministratorAsync(id, groupId, request.UserId!.Value, ct)) : Unauthorized();

	[HttpGet("icon-presets")]
	public async Task<IActionResult> Presets(CancellationToken ct) =>
	 currentUser.UserId is Guid ? Ok(await icons.GetPresetsAsync(ct)) : Unauthorized();

	[HttpGet("{groupId:guid}/icon")]
	public async Task<IActionResult> Icon(Guid groupId, CancellationToken ct)
	{
		if (currentUser.UserId is not Guid id) return Unauthorized();
		var result = await groups.GetAsync(id, groupId, ct);
		if (result.Value is null) return Result(result);
		var key = result.Value.IconStorageKey ?? result.Value.Icon;
		if (key is null || result.Value.IconStorageKey is null && !await icons.IsPresetAsync(key, ct)) return NotFound();
		return Ok(await icons.GetUrlAsync(key, ct));
	}
	[HttpPost, Consumes("multipart/form-data"), RequestSizeLimit(6 * 1024 * 1024)]
	public async Task<IActionResult> CreateForm([FromForm] GroupFormRequest form, CancellationToken ct)
	{
		if (currentUser.UserId is not Guid id) return Unauthorized();
		if (form.RemoveIcon) return BadRequest();
		await using var stream = form.IconFile?.OpenReadStream();
		var result = await groups.CreateAsync(id, new CreateGroupRequest { Name = form.Name, CurrencyCode = form.CurrencyCode!, Description = form.Description, Icon = form.Icon, IconContent = stream, IconContentType = form.IconFile?.ContentType }, ct);
		return result.Status == 201 ? CreatedAtAction(nameof(Get), new { groupId = result.Value!.Id }, result.Value) : Result(result);
	}
	[HttpPut("{groupId:guid}"), Consumes("multipart/form-data"), RequestSizeLimit(6 * 1024 * 1024)]
	public async Task<IActionResult> UpdateForm(Guid groupId, [FromForm] GroupFormRequest form, CancellationToken ct)
	{
		if (currentUser.UserId is not Guid id) return Unauthorized();
		await using var stream = form.IconFile?.OpenReadStream();
		return Result(await groups.UpdateAsync(id, groupId, new UpdateGroupRequest { Name = form.Name, Description = form.Description, Icon = form.Icon, RemoveIcon = form.RemoveIcon, IconContent = stream, IconContentType = form.IconFile?.ContentType }, ct));
	}
}
