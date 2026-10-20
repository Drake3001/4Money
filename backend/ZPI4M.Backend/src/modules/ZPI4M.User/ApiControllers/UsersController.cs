using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ZPI4M.User.DTOs.EmailConfirmation;
using ZPI4M.User.DTOs.Registration;
using ZPI4M.User.Services.EmailConfirmation;
using ZPI4M.User.Services.Registration;

namespace ZPI4M.User.ApiControllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
	private readonly IUserRegistrationService _registrationService;
	private readonly IEmailConfirmationService _emailConfirmationService;

	public UsersController(IUserRegistrationService registrationService, IEmailConfirmationService emailConfirmationService)
	{
		_registrationService = registrationService;
		_emailConfirmationService = emailConfirmationService;
	}

	[HttpPost("register")]
	[AllowAnonymous]
	[EnableRateLimiting("user-email")]
	[ProducesResponseType(typeof(RegisterUserResponse), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<IActionResult> Register(
		[FromBody] RegisterUserRequest request,
		CancellationToken cancellationToken)
	{
		var (result, user) = await _registrationService.RegisterAsync(request, cancellationToken);
		return result switch
		{
			UserRegistrationResult.Registered => StatusCode(StatusCodes.Status201Created, user),
			UserRegistrationResult.LoginOrEmailAlreadyInUse => Problem(
				statusCode: StatusCodes.Status409Conflict, title: "Login or email is already in use."),
			UserRegistrationResult.EmailDeliveryFailed => Problem(
				statusCode: StatusCodes.Status503ServiceUnavailable, title: "Account created, but confirmation email could not be sent."),
			_ => throw new InvalidOperationException("Unknown registration result.")
		};
	}

	[HttpPost("confirm-email")]
	[AllowAnonymous]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> ConfirmEmail(
		[FromBody] ConfirmEmailRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _emailConfirmationService.ConfirmAsync(request, cancellationToken);
		return result switch
		{
			EmailConfirmationResult.Confirmed => NoContent(),
			EmailConfirmationResult.InvalidOrExpiredToken => Problem(
				statusCode: StatusCodes.Status400BadRequest, title: "Invalid, expired or already used confirmation token."),
			_ => throw new InvalidOperationException("Unknown email confirmation result.")
		};
	}

	[HttpPost("resend-confirmation")]
	[AllowAnonymous]
	[EnableRateLimiting("user-email")]
	[ProducesResponseType(StatusCodes.Status202Accepted)]
	[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<IActionResult> ResendConfirmation(
		[FromBody] ResendEmailConfirmationRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _emailConfirmationService.ResendAsync(request, cancellationToken);
		return result switch
		{
			ResendEmailConfirmationResult.Accepted => Accepted(),
			ResendEmailConfirmationResult.EmailDeliveryFailed => Problem(
				statusCode: StatusCodes.Status503ServiceUnavailable, title: "Confirmation email could not be sent."),
			_ => throw new InvalidOperationException("Unknown email confirmation resend result.")
		};
	}
}
