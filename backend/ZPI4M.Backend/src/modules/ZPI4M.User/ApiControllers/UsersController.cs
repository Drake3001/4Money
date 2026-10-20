using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
	[ProducesResponseType(StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	public async Task<IActionResult> Register(
		[FromBody] RegisterUserRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _registrationService.RegisterAsync(request, cancellationToken);
		return result switch
		{
			UserRegistrationResult.Registered => StatusCode(StatusCodes.Status201Created),
			UserRegistrationResult.LoginOrEmailAlreadyInUse => Problem(
				statusCode: StatusCodes.Status409Conflict,
				title: "Login or email is already in use."),
			_ => throw new InvalidOperationException("Unknown registration result.")
		};
	}

	[HttpPost("confirm-email")]
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
				statusCode: StatusCodes.Status400BadRequest,
				title: "Email confirmation token is invalid, expired or already used."),
			_ => throw new InvalidOperationException("Unknown email confirmation result.")
		};
	}
}
