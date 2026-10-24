using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.User.ApiControllers;
using ZPI4M.User.DTOs.AccountDeactivation;
using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.DTOs.EmailChange;
using ZPI4M.User.DTOs.EmailConfirmation;
using ZPI4M.User.DTOs.PasswordReset;
using ZPI4M.User.DTOs.Profile;
using ZPI4M.User.DTOs.Registration;
using ZPI4M.User.Services.AccountDeactivation;
using ZPI4M.User.Services.Authentication;
using ZPI4M.User.Services.EmailChange;
using ZPI4M.User.Services.EmailConfirmation;
using ZPI4M.User.Services.PasswordReset;
using ZPI4M.User.Services.Profile;
using ZPI4M.User.Services.RateLimiting;
using ZPI4M.User.Services.Registration;

namespace ZPI4M.User.Tests;

public sealed partial class UserControllerTests
{
	[Theory]
	[InlineData("GET", "profile")]
	[InlineData("PUT", "profile")]
	[InlineData("POST", "logout")]
	[InlineData("POST", "profile/change-email")]
	[InlineData("POST", "profile/deactivate")]
	public async Task AnonymousRequestsAreRejectedBeforeCallingServices(string method, string route)
	{
		await using var host = await TestHost.StartAsync();
		using var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/users/" + route));
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.Equal(0, host.Services.Calls);
	}

	[Theory]
	[InlineData("register")]
	[InlineData("login")]
	[InlineData("refresh")]
	[InlineData("confirm-email")]
	[InlineData("resend-confirmation")]
	[InlineData("forgot-password")]
	[InlineData("reset-password")]
	[InlineData("confirm-email-change")]
	[InlineData("confirm-deactivation")]
	[InlineData("profile/change-email")]
	public async Task InvalidBodiesReturnValidationProblemsWithoutCallingServices(string route)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		using var response = await host.Client.PostAsJsonAsync("/api/users/" + route, new { });
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		var json = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.True(json.TryGetProperty("errors", out _));
		Assert.Equal(0, host.Services.Calls);
	}

	[Theory]
	[InlineData("register", 0, 201)]
	[InlineData("register", 1, 409)]
	[InlineData("register", 2, 503)]
	[InlineData("login", 0, 200)]
	[InlineData("login", 1, 401)]
	[InlineData("login", 2, 403)]
	[InlineData("confirm-email", 0, 204)]
	[InlineData("confirm-email", 1, 400)]
	[InlineData("resend-confirmation", 0, 202)]
	[InlineData("resend-confirmation", 1, 503)]
	[InlineData("reset-password", 0, 204)]
	[InlineData("reset-password", 1, 400)]
	[InlineData("profile/change-email", 0, 202)]
	[InlineData("profile/change-email", 1, 401)]
	[InlineData("profile/change-email", 2, 400)]
	[InlineData("profile/change-email", 3, 400)]
	[InlineData("profile/change-email", 4, 409)]
	[InlineData("profile/change-email", 5, 429)]
	[InlineData("profile/change-email", 6, 503)]
	[InlineData("confirm-email-change", 0, 204)]
	[InlineData("confirm-email-change", 1, 400)]
	[InlineData("confirm-email-change", 2, 409)]
	[InlineData("profile/deactivate", 0, 202)]
	[InlineData("profile/deactivate", 1, 401)]
	[InlineData("profile/deactivate", 2, 429)]
	[InlineData("profile/deactivate", 3, 503)]
	[InlineData("confirm-deactivation", 0, 204)]
	[InlineData("confirm-deactivation", 1, 400)]
	public async Task ServiceResultsMapToPublicHttpContract(string route, int result, int status)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		host.Services.Result = result;
		using var response = await host.Client.PostAsJsonAsync("/api/users/" + route, ValidBody());
		Assert.Equal(status, (int)response.StatusCode);
		Assert.Equal(1, host.Services.Calls);
		if (status >= 400)
		{
			Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
			Assert.Equal(status, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
		}
	}

	[Theory]
	[InlineData(0, 200)]
	[InlineData(1, 401)]
	[InlineData(2, 409)]
	public async Task ProfileUpdateUsesAuthenticatedOwnerAndMapsResults(int result, int status)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		host.Services.Result = result;
		using var response = await host.Client.PutAsJsonAsync($"/api/users/profile?userId={Guid.NewGuid()}", ValidBody());
		Assert.Equal(status, (int)response.StatusCode);
		Assert.Equal(host.UserId, host.Services.LastUserId);
	}

	[Fact]
	public async Task ProfileAndLogoutUseClaimsAndDoNotExposeInternalFields()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		using var response = await host.Client.GetAsync($"/api/users/profile?userId={Guid.NewGuid()}");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
		Assert.Equal(host.UserId, host.Services.LastUserId);
		var profile = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(new[] { "id", "login", "name", "surname", "email", "phone", "birthDate", "icon", "isEmailConfirmed", "createdAt", "lastLoginTime" },
			profile.EnumerateObject().Select(p => p.Name));
		using var logout = await host.Client.PostAsync("/api/users/logout", null);
		Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
		Assert.Equal(host.UserId, host.Services.LastUserId);
		Assert.Equal(host.SessionId, host.Services.LastSessionId);
	}

	[Theory]
	[InlineData(0, 200)]
	[InlineData(1, 401)]
	public async Task RefreshReturnsUncacheableTokenOrUnauthorized(int result, int status)
	{
		await using var host = await TestHost.StartAsync();
		host.Services.Result = result;
		using var response = await host.Client.PostAsJsonAsync("/api/users/refresh", ValidBody());
		Assert.Equal(status, (int)response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
	}

	private static object ValidBody() => new
	{
		login = "test.user",
		name = "Anna",
		surname = "Testowa",
		email = "test@example.test",
		password = "Password1!",
		currentPassword = "Password1!",
		newPassword = "Password2!",
		newEmail = "new@example.test",
		birthDate = "2000-01-01",
		token = new string('a', 43),
		refreshToken = new string('b', 43)
	};

	private sealed class TestHost(WebApplication app, HttpClient client, FakeServices services) : IAsyncDisposable
	{
		public Guid UserId { get; } = Guid.NewGuid();
		public Guid SessionId { get; } = Guid.NewGuid();
		public HttpClient Client { get; } = client;
		public FakeServices Services { get; } = services;
		public void Authenticate()
		{
			Client.DefaultRequestHeaders.Add("X-Test-User", UserId.ToString());
			Client.DefaultRequestHeaders.Add("X-Test-Session", SessionId.ToString());
		}
		public static async Task<TestHost> StartAsync(Dictionary<string, string?>? configuration = null)
		{
			var builder = WebApplication.CreateBuilder();
			builder.WebHost.UseTestServer();
			builder.Logging.ClearProviders();
			if (configuration is not null)
				builder.Configuration.AddInMemoryCollection(configuration);
			builder.Services.AddUserRateLimiting(builder.Configuration);
			builder.Services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly);
			builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
			builder.Services.AddAuthorization();
			var services = new FakeServices();
			builder.Services.AddSingleton<IUserRegistrationService>(services);
			builder.Services.AddSingleton<IUserAuthenticationService>(services);
			builder.Services.AddSingleton<IEmailConfirmationService>(services);
			builder.Services.AddSingleton<IUserProfileService>(services);
			builder.Services.AddSingleton<IEmailChangeService>(services);
			builder.Services.AddSingleton<IPasswordResetService>(services);
			builder.Services.AddSingleton<IAccountDeactivationService>(services);
			var app = builder.Build();
			// Only this test host accepts a header for selecting the simulated remote IP.
			app.Use(async (context, next) =>
			{
				context.Connection.RemoteIpAddress = context.Request.Headers.TryGetValue("X-Test-IP", out var address)
					? IPAddress.Parse(address.ToString()) : IPAddress.Loopback;
				await next(context);
			});
			app.UseRouting();
			app.UseRateLimiter();
			app.UseAuthentication();
			app.UseAuthorization();
			app.MapControllers();
			try
			{
				await app.StartAsync();
			}
			catch
			{
				await app.DisposeAsync();
				throw;
			}
			return new(app, app.GetTestClient(), services);
		}
		public async ValueTask DisposeAsync()
		{
			Client.Dispose();
			await app.DisposeAsync();
		}
	}

	private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue("X-Test-User", out var user))
				return Task.FromResult(AuthenticateResult.NoResult());
			var principal = new ClaimsPrincipal(new ClaimsIdentity([
				new Claim("sub", user.ToString()), new Claim("sid", Request.Headers["X-Test-Session"].ToString())], Scheme.Name));
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
		}
	}

	private sealed class FakeServices : IUserRegistrationService, IUserAuthenticationService, IEmailConfirmationService,
		IUserProfileService, IEmailChangeService, IPasswordResetService, IAccountDeactivationService
	{
		public int Result { get; set; }
		public int Calls { get; private set; }
		public Guid LastUserId { get; private set; }
		public Guid LastSessionId { get; private set; }
		private T Record<T>(T value, CancellationToken cancellationToken, Guid userId = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Calls++;
			LastUserId = userId;
			return value;
		}
		private static AuthenticationResponse Authentication() => new("access", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(7));
		private static UserProfileResponse Profile(Guid id) => new(id, "test.user", "Anna", "Testowa", "test@example.test", null, new DateOnly(2000, 1, 1), null, true, DateTime.UtcNow, null);
		public Task<(UserRegistrationResult Result, RegisterUserResponse? User)> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken) =>
			Task.FromResult(Record<(UserRegistrationResult, RegisterUserResponse?)>(((UserRegistrationResult)Result,
				new(Guid.NewGuid(), request.Login, request.Name, request.Surname, request.Email, request.BirthDate, false, DateTime.UtcNow)), cancellationToken));
		public Task<(LoginResult Result, AuthenticationResponse? Response)> LoginAsync(LoginRequest request, CancellationToken cancellationToken) =>
			Task.FromResult(Record<(LoginResult, AuthenticationResponse?)>(((LoginResult)Result, Result == 0 ? Authentication() : null), cancellationToken));
		public Task<AuthenticationResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken) =>
			Task.FromResult(Record<AuthenticationResponse?>(Result == 0 ? Authentication() : null, cancellationToken));
		public Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
		{
			Record(0, cancellationToken, userId);
			LastSessionId = sessionId;
			return Task.CompletedTask;
		}
		public Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) => throw new InvalidOperationException("Test scheme does not validate real sessions.");
		public Task<EmailConfirmationResult> ConfirmAsync(ConfirmEmailRequest request, CancellationToken cancellationToken) => Task.FromResult(Record((EmailConfirmationResult)Result, cancellationToken));
		public Task<ResendEmailConfirmationResult> ResendAsync(ResendEmailConfirmationRequest request, CancellationToken cancellationToken) => Task.FromResult(Record((ResendEmailConfirmationResult)Result, cancellationToken));
		public Task<UserProfileResponse?> GetAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Record<UserProfileResponse?>(Profile(userId), cancellationToken, userId));
		public Task<(UpdateUserProfileResult Result, UserProfileResponse? Profile)> UpdateAsync(Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken) =>
			Task.FromResult(Record<(UpdateUserProfileResult, UserProfileResponse?)>(((UpdateUserProfileResult)Result, Profile(userId)), cancellationToken, userId));
		public Task<ChangeEmailResult> RequestAsync(Guid userId, ChangeEmailRequest request, CancellationToken cancellationToken) => Task.FromResult(Record((ChangeEmailResult)Result, cancellationToken, userId));
		public Task<ConfirmEmailChangeResult> ConfirmAsync(ConfirmEmailChangeRequest request, CancellationToken cancellationToken) => Task.FromResult(Record((ConfirmEmailChangeResult)Result, cancellationToken));
		public Task RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
		{
			Record(0, cancellationToken);
			return Task.CompletedTask;
		}
		public Task<PasswordResetResult> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken) => Task.FromResult(Record((PasswordResetResult)Result, cancellationToken));
		public Task<RequestAccountDeactivationResult> RequestAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Record((RequestAccountDeactivationResult)Result, cancellationToken, userId));
		public Task<ConfirmAccountDeactivationResult> ConfirmAsync(ConfirmAccountDeactivationRequest request, CancellationToken cancellationToken) => Task.FromResult(Record((ConfirmAccountDeactivationResult)Result, cancellationToken));
	}
}
