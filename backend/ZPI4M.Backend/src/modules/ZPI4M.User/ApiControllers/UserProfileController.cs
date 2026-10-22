using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.User.DTOs.Profile;
using ZPI4M.User.Services.Profile;

namespace ZPI4M.User.ApiControllers;

[ApiController]
[Authorize]
[Route("api/users/profile")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UserProfileController(IUserProfileService profileService) : ControllerBase
{
	[HttpGet]
	[ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
	{
		var userId = Guid.Parse(User.FindFirst("sub")!.Value);
		var profile = await profileService.GetAsync(userId, cancellationToken);
		return profile is not null ? Ok(profile) : Problem(
			statusCode: StatusCodes.Status401Unauthorized, title: "Account is no longer available.");
	}

	[HttpPut]
	[ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserProfileRequest request, CancellationToken cancellationToken)
	{
		var userId = Guid.Parse(User.FindFirst("sub")!.Value);
		var (result, profile) = await profileService.UpdateAsync(userId, request, cancellationToken);
		return result switch
		{
			UpdateUserProfileResult.Updated => Ok(profile),
			UpdateUserProfileResult.UserUnavailable => Problem(
				statusCode: StatusCodes.Status401Unauthorized, title: "Account is no longer available."),
			UpdateUserProfileResult.LoginAlreadyInUse => Problem(
				statusCode: StatusCodes.Status409Conflict, title: "Login is already in use."),
			_ => throw new InvalidOperationException("Unknown profile update result.")
		};
	}
}
