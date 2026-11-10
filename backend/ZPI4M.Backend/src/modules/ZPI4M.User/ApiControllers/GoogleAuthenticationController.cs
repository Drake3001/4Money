using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.DTOs.Registration;
using ZPI4M.User.Integrations.Google;
using ZPI4M.User.Services.GoogleAuthentication;
using ZPI4M.User.Services.RateLimiting;

namespace ZPI4M.User.ApiControllers;

[ApiController]
[AllowAnonymous]
[Route("api/users/google")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(UserRateLimitingPolicies.Login)]
public sealed class GoogleAuthenticationController(
	IGoogleAuthenticationChallengeStore challenges,
	IGoogleAuthenticationService authentication,
	IOptions<GoogleAuthenticationOptions> options) : ControllerBase
{
	private const string ChallengeCookie = "4m_google_challenge";
	private const string CookiePath = "/api/users/google";

	[HttpPost("challenge")]
	[ProducesResponseType(typeof(GoogleChallengeResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public IActionResult CreateChallenge()
	{
		try
		{
			var challenge = challenges.Create();
			Response.Cookies.Append(ChallengeCookie, challenge.BrowserToken, new CookieOptions
			{
				HttpOnly = true,
				Secure = Request.IsHttps,
				SameSite = SameSiteMode.Strict,
				Path = CookiePath,
				Expires = challenge.ExpiresAt,
				IsEssential = true
			});
			return Ok(new GoogleChallengeResponse(options.Value.ClientId, challenge.Nonce, challenge.ExpiresAt));
		}
		catch (GoogleIdentityUnavailableException)
		{
			return Unavailable();
		}
	}

	[HttpPost("login")]
	[ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<IActionResult> Login([FromBody] GoogleLoginRequest request, CancellationToken cancellationToken)
	{
		if (!options.Value.Enabled)
			return Unavailable();
		try
		{
			var (result, response) = await authentication.LoginAsync(request, BrowserToken, cancellationToken);
			if (result != GoogleAuthenticationResult.LoggedIn)
				return Failure(result);
			DeleteChallengeCookie();
			return Ok(response);
		}
		catch (GoogleIdentityUnavailableException)
		{
			return Unavailable();
		}
	}

	[HttpPost("register")]
	[EnableRateLimiting("user-email")]
	[ProducesResponseType(typeof(GoogleRegistrationResponse), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<IActionResult> Register([FromBody] GoogleRegistrationRequest request, CancellationToken cancellationToken)
	{
		if (!options.Value.Enabled)
			return Unavailable();
		try
		{
			var (result, response) = await authentication.RegisterAsync(request, BrowserToken, cancellationToken);
			if (result != GoogleAuthenticationResult.Registered)
				return Failure(result);
			DeleteChallengeCookie();
			return StatusCode(StatusCodes.Status201Created, response);
		}
		catch (GoogleIdentityUnavailableException)
		{
			return Unavailable();
		}
	}

	private string BrowserToken => Request.Cookies[ChallengeCookie] ?? string.Empty;

	private void DeleteChallengeCookie() => Response.Cookies.Delete(ChallengeCookie, new CookieOptions
	{
		Path = CookiePath,
		HttpOnly = true,
		Secure = Request.IsHttps,
		SameSite = SameSiteMode.Strict
	});

	private ObjectResult Unavailable() => Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
		title: "Google authentication is temporarily unavailable.");

	private ObjectResult Failure(GoogleAuthenticationResult result)
	{
		var (status, title, code) = result switch
		{
			GoogleAuthenticationResult.InvalidCredentials => (401, "Invalid Google credentials or expired challenge.", "invalid_google_credentials"),
			GoogleAuthenticationResult.RegistrationRequired => (409, "Complete Google registration.", "google_registration_required"),
			GoogleAuthenticationResult.AccountInactive => (403, "Account is deactivated.", "account_inactive"),
			GoogleAuthenticationResult.EmailConfirmationRequired => (403, "Email confirmation is required.", "email_confirmation_required"),
			GoogleAuthenticationResult.AccountAlreadyExists => (409, "Google account is already registered. Sign in instead.", "google_account_already_exists"),
			GoogleAuthenticationResult.LoginOrEmailAlreadyInUse => (409, "Login or email is already in use.", "login_or_email_in_use"),
			GoogleAuthenticationResult.EmailDeliveryFailed => (503, "Account created, but confirmation email could not be sent.", "email_delivery_failed"),
			_ => throw new InvalidOperationException("Unknown Google authentication result.")
		};
		return Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
	}
}
