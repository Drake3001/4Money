using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.User.DTOs.Registration;
using ZPI4M.User.Services.Registration;

namespace ZPI4M.User.ApiControllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
	private readonly IUserRegistrationService _registrationService;

	public UsersController(IUserRegistrationService registrationService)
	{
		_registrationService = registrationService;
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
}
