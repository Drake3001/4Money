using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.User.DTOs.EmailChange;
using ZPI4M.User.Services.EmailChange;

namespace ZPI4M.User.ApiControllers;

[ApiController]
[Route("api/users")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmailChangeController(IEmailChangeService emailChangeService, ICurrentUser currentUser) : ControllerBase
{
	[HttpPost("profile/change-email")]
	[Authorize]
	[EnableRateLimiting("user-email")]
	[ProducesResponseType(StatusCodes.Status202Accepted)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<IActionResult> RequestChange([FromBody] ChangeEmailRequest request, CancellationToken cancellationToken)
	{
		if (currentUser.UserId is not Guid userId)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user is unavailable.");
		var result = await emailChangeService.RequestAsync(userId, request, cancellationToken);
		return result switch
		{
			ChangeEmailResult.Accepted => Accepted(),
			ChangeEmailResult.UserUnavailable => Problem(
				statusCode: StatusCodes.Status401Unauthorized, title: "Account is no longer available."),
			ChangeEmailResult.InvalidPassword => Problem(
				statusCode: StatusCodes.Status400BadRequest, title: "Current password is incorrect."),
			ChangeEmailResult.EmailUnchanged => Problem(
				statusCode: StatusCodes.Status400BadRequest, title: "New email must differ from the current address."),
			ChangeEmailResult.EmailAlreadyInUse => Problem(
				statusCode: StatusCodes.Status409Conflict, title: "Email is already in use."),
			ChangeEmailResult.CooldownActive => Problem(
				statusCode: StatusCodes.Status429TooManyRequests, title: "Please wait before requesting another email change."),
			ChangeEmailResult.EmailDeliveryFailed => Problem(
				statusCode: StatusCodes.Status503ServiceUnavailable, title: "Email change confirmation could not be sent. Your email has not changed."),
			_ => throw new InvalidOperationException("Unknown email change result.")
		};
	}

	[HttpPost("confirm-email-change")]
	[AllowAnonymous]
	[EnableRateLimiting("user-email")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
	public async Task<IActionResult> ConfirmChange([FromBody] ConfirmEmailChangeRequest request, CancellationToken cancellationToken)
	{
		var result = await emailChangeService.ConfirmAsync(request, cancellationToken);
		return result switch
		{
			ConfirmEmailChangeResult.Confirmed => NoContent(),
			ConfirmEmailChangeResult.InvalidOrExpiredToken => Problem(
				statusCode: StatusCodes.Status400BadRequest, title: "Invalid, expired or already used email change token."),
			ConfirmEmailChangeResult.EmailAlreadyInUse => Problem(
				statusCode: StatusCodes.Status409Conflict, title: "Email is already in use."),
			_ => throw new InvalidOperationException("Unknown email change confirmation result.")
		};
	}
}
