using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ZPI4M.User.DTOs.AccountDeactivation;
using ZPI4M.User.Services.AccountDeactivation;

namespace ZPI4M.User.ApiControllers;

[ApiController]
[Route("api/users")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountDeactivationController(IAccountDeactivationService deactivationService) : ControllerBase
{
	[HttpPost("profile/deactivate")]
	[Authorize]
	[EnableRateLimiting("user-email")]
	[ProducesResponseType(StatusCodes.Status202Accepted)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<IActionResult> RequestDeactivation(CancellationToken cancellationToken)
	{
		var userId = Guid.Parse(User.FindFirst("sub")!.Value);
		var result = await deactivationService.RequestAsync(userId, cancellationToken);
		return result switch
		{
			RequestAccountDeactivationResult.Accepted => Accepted(),
			RequestAccountDeactivationResult.UserUnavailable => Problem(
				statusCode: StatusCodes.Status401Unauthorized, title: "Account is no longer available."),
			RequestAccountDeactivationResult.CooldownActive => Problem(
				statusCode: StatusCodes.Status429TooManyRequests, title: "Please wait before requesting another deactivation email."),
			RequestAccountDeactivationResult.EmailDeliveryFailed => Problem(
				statusCode: StatusCodes.Status503ServiceUnavailable, title: "Deactivation email could not be sent. Your account remains active."),
			_ => throw new InvalidOperationException("Unknown account deactivation request result.")
		};
	}

	[HttpPost("confirm-deactivation")]
	[AllowAnonymous]
	[EnableRateLimiting("user-email")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
	public async Task<IActionResult> ConfirmDeactivation([FromBody] ConfirmAccountDeactivationRequest request, CancellationToken cancellationToken)
	{
		var result = await deactivationService.ConfirmAsync(request, cancellationToken);
		return result switch
		{
			ConfirmAccountDeactivationResult.Deactivated => NoContent(),
			ConfirmAccountDeactivationResult.InvalidOrExpiredToken => Problem(
				statusCode: StatusCodes.Status400BadRequest, title: "Invalid, expired or already used account deactivation token."),
			_ => throw new InvalidOperationException("Unknown account deactivation confirmation result.")
		};
	}
}
