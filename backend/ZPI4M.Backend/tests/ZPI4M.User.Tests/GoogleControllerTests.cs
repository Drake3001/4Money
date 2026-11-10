using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.DTOs.Registration;
using ZPI4M.User.Integrations.Google;
using ZPI4M.User.Services.GoogleAuthentication;

namespace ZPI4M.User.Tests;

public sealed partial class UserControllerTests
{
	[Theory]
	[InlineData("login", GoogleAuthenticationResult.LoggedIn, 200)]
	[InlineData("login", GoogleAuthenticationResult.InvalidCredentials, 401)]
	[InlineData("login", GoogleAuthenticationResult.RegistrationRequired, 409)]
	[InlineData("login", GoogleAuthenticationResult.AccountInactive, 403)]
	[InlineData("login", GoogleAuthenticationResult.EmailConfirmationRequired, 403)]
	[InlineData("register", GoogleAuthenticationResult.Registered, 201)]
	[InlineData("register", GoogleAuthenticationResult.AccountAlreadyExists, 409)]
	[InlineData("register", GoogleAuthenticationResult.LoginOrEmailAlreadyInUse, 409)]
	[InlineData("register", GoogleAuthenticationResult.EmailDeliveryFailed, 503)]
	public async Task GoogleResultsMapToUncacheableHttpResponses(string route, GoogleAuthenticationResult result, int status)
	{
		await using var host = await TestHost.StartAsync();
		host.Services.Result = (int)result;
		using var response = await host.Client.PostAsJsonAsync("/api/users/google/" + route, new
		{
			idToken = "synthetic-token",
			login = "google.user",
			name = "Anna",
			surname = "Testowa",
			birthDate = "2000-01-01"
		});
		Assert.Equal(status, (int)response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
		Assert.Equal(1, host.Services.Calls);
		if (status >= 400)
		{
			var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
			Assert.True(problem.TryGetProperty("code", out _));
		}
		else
			Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("4m_google_challenge=;"));
	}

	[Fact]
	public async Task ChallengeKeepsBrowserSecretOutOfJsonAndSetsSecureHttpOnlyCookie()
	{
		await using var host = await TestHost.StartAsync();
		host.Client.BaseAddress = new Uri("https://localhost");
		using var response = await host.Client.PostAsync("/api/users/google/challenge", null);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
		var json = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(new[] { "clientId", "nonce", "expiresAt" }, json.EnumerateObject().Select(property => property.Name));
		var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
		Assert.Contains("httponly", cookie);
		Assert.Contains("secure", cookie);
		Assert.Contains("samesite=strict", cookie);
		Assert.Contains("path=/api/users/google", cookie);
		Assert.DoesNotContain(json.GetProperty("nonce").GetString()!, cookie);
	}

	[Theory]
	[InlineData("login")]
	[InlineData("register")]
	public async Task InvalidGoogleBodiesNeverCallTheService(string route)
	{
		await using var host = await TestHost.StartAsync();
		using var response = await host.Client.PostAsJsonAsync("/api/users/google/" + route, new { });
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal(0, host.Services.Calls);
	}

	[Theory]
	[InlineData("challenge")]
	[InlineData("login")]
	[InlineData("register")]
	public async Task DisabledGoogleEndpointsReturnUnavailableWithoutCallingService(string route)
	{
		await using var host = await TestHost.StartAsync(new Dictionary<string, string?> { ["GoogleAuthentication:Enabled"] = "false" });
		using var response = await host.Client.PostAsJsonAsync("/api/users/google/" + route, new
		{
			idToken = "synthetic",
			login = "google.user",
			name = "Anna",
			surname = "Testowa",
			birthDate = "2000-01-01"
		});
		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.Equal(0, host.Services.Calls);
	}

	[Fact]
	public async Task GoogleMetadataOutageMapsToUnavailable()
	{
		await using var host = await TestHost.StartAsync();
		host.Services.GoogleUnavailable = true;
		using var response = await host.Client.PostAsJsonAsync("/api/users/google/login", new { idToken = "synthetic" });
		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
	}

	[Fact]
	public async Task GoogleChallengeAndLoginShareTheLocalLoginRateLimit()
	{
		await using var host = await TestHost.StartAsync(SmallLimits());
		using var challenge = await host.Client.PostAsync("/api/users/google/challenge", null);
		Assert.Equal(HttpStatusCode.OK, challenge.StatusCode);
		using var local = await host.Client.PostAsJsonAsync("/api/users/login", ValidBody());
		Assert.Equal(HttpStatusCode.OK, local.StatusCode);
		using var google = await host.Client.PostAsJsonAsync("/api/users/google/login", new { idToken = "synthetic" });
		Assert.Equal(HttpStatusCode.TooManyRequests, google.StatusCode);
		Assert.Equal(1, host.Services.Calls);
	}

	private sealed partial class FakeServices
	{
		public bool GoogleUnavailable { get; set; }

		public Task<(GoogleAuthenticationResult Result, AuthenticationResponse? Response)> LoginAsync(
			GoogleLoginRequest request, string browserToken, CancellationToken cancellationToken)
		{
			if (GoogleUnavailable)
				throw new GoogleIdentityUnavailableException("Synthetic outage.");
			return Task.FromResult(Record<(GoogleAuthenticationResult, AuthenticationResponse?)>(
				((GoogleAuthenticationResult)Result, Authentication()), cancellationToken));
		}

		public Task<(GoogleAuthenticationResult Result, GoogleRegistrationResponse? Response)> RegisterAsync(
			GoogleRegistrationRequest request, string browserToken, CancellationToken cancellationToken) =>
			Task.FromResult(Record<(GoogleAuthenticationResult, GoogleRegistrationResponse?)>(
				((GoogleAuthenticationResult)Result, new GoogleRegistrationResponse(
					new RegisterUserResponse(Guid.NewGuid(), request.Login, request.Name, request.Surname,
						"test@gmail.com", request.BirthDate, true, DateTime.UtcNow), Authentication(), false)), cancellationToken));
	}
}
