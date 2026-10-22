using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ZPI4M.User.DTOs.PasswordReset;
using ZPI4M.User.Services.PasswordReset;

namespace ZPI4M.User.ApiControllers;

[ApiController]
[AllowAnonymous]
[Route("api/users")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PasswordResetController(IPasswordResetService passwordResetService) : ControllerBase
{
	[HttpPost("forgot-password")]
	[EnableRateLimiting("user-email")]
	[ProducesResponseType(StatusCodes.Status202Accepted)]
	[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
	public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
	{
		await passwordResetService.RequestAsync(request, cancellationToken);
		return Accepted();
	}

	[HttpPost("reset-password")]
	[EnableRateLimiting("user-auth")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
	public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
	{
		var result = await passwordResetService.ResetAsync(request, cancellationToken);
		return result switch
		{
			PasswordResetResult.Reset => NoContent(),
			PasswordResetResult.InvalidOrExpiredToken => Problem(
				statusCode: StatusCodes.Status400BadRequest, title: "Invalid, expired or already used password reset token."),
			_ => throw new InvalidOperationException("Unknown password reset result.")
		};
	}
}
