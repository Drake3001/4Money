using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Storage;
using ZPI4M.User.DTOs.Profile;
using ZPI4M.User.Services.Profile;

namespace ZPI4M.User.ApiControllers;

[ApiController]
[Authorize]
[Route("api/users/profile")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(UserIconExceptionFilter))]
public sealed class UserProfileController(IUserProfileService profileService, ICurrentUser currentUser, IUserIconService icons) : ControllerBase
{
	[HttpGet]
	[ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not Guid userId)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user is unavailable.");
		var profile = await profileService.GetAsync(userId, cancellationToken);
		return profile is not null ? Ok(profile) : Problem(
			statusCode: StatusCodes.Status401Unauthorized, title: "Account is no longer available.");
	}

	[HttpGet("icon-presets")]
	[ProducesResponseType(typeof(IReadOnlyList<UserIconPreset>), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<IActionResult> GetIconPresets(CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not Guid)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user is unavailable.");
		return Ok(await icons.GetPresetsAsync(cancellationToken));
	}

	[HttpGet("icon")]
	[ProducesResponseType(typeof(FileUrlResult), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<IActionResult> GetIcon(CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not Guid userId)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user is unavailable.");
		var profile = await profileService.GetAsync(userId, cancellationToken);
		if (profile is null)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Account is no longer available.");
		var key = profile.IconStorageKey ?? profile.Icon;
		if (key is null || profile.IconStorageKey is null && !await icons.IsPresetAsync(key, cancellationToken))
			return Problem(statusCode: StatusCodes.Status404NotFound, title: "Profile image is unavailable.");
		return Ok(await icons.GetUrlAsync(key, cancellationToken));
	}

	[HttpPut, Consumes("application/json")]
	[ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserProfileRequest request, CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not Guid userId)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user is unavailable.");
		return await ApplyUpdateAsync(userId, request, cancellationToken);
	}

	// Prefer JSON when Content-Type is absent; Consumes still selects multipart for uploads.
	[HttpPut(Order = 1), Consumes("multipart/form-data")]
	[RequestSizeLimit(6 * 1024 * 1024)]
	[RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
	[ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<IActionResult> UpdateProfileForm([FromForm] UpdateUserProfileFormRequest form, CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not Guid userId)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user is unavailable.");
		await using var stream = form.IconFile?.OpenReadStream();
		return await ApplyUpdateAsync(userId, new UpdateUserProfileRequest
		{
			Login = form.Login,
			Name = form.Name,
			Surname = form.Surname,
			Phone = form.Phone,
			BirthDate = form.BirthDate,
			Icon = form.Icon,
			RemoveIcon = form.RemoveIcon,
			IconContent = stream,
			IconContentType = form.IconFile?.ContentType
		}, cancellationToken);
	}

	private async Task<IActionResult> ApplyUpdateAsync(Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken)
	{
		var (result, profile) = await profileService.UpdateAsync(userId, request, cancellationToken);
		return result switch
		{
			UpdateUserProfileResult.Updated => Ok(profile),
			UpdateUserProfileResult.UserUnavailable => Problem(
				statusCode: StatusCodes.Status401Unauthorized, title: "Account is no longer available."),
			UpdateUserProfileResult.LoginAlreadyInUse => Problem(
				statusCode: StatusCodes.Status409Conflict, title: "Login is already in use."),
			UpdateUserProfileResult.InvalidRequest => ValidationProblem(new ValidationProblemDetails(
				new Dictionary<string, string[]> { ["request"] = ["Invalid profile data or icon selection."] })),
			_ => throw new InvalidOperationException("Unknown profile update result.")
		};
	}
}
