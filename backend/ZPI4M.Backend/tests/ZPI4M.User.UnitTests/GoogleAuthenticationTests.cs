using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using ZPI4M.User.DTOs.Registration;
using ZPI4M.User.Integrations.Google;
using ZPI4M.User.Services.GoogleAuthentication;

namespace ZPI4M.User.UnitTests;

public sealed class GoogleAuthenticationTests
{
	[Fact]
	public async Task ChallengeIsBoundToBrowserAndConsumedOnlyOnceUnderConcurrency()
	{
		using var store = new GoogleAuthenticationChallengeStore(Options.Create(new GoogleAuthenticationOptions { Enabled = true }), TimeProvider.System);
		var challenge = store.Create();
		var other = store.Create();
		Assert.NotEqual(challenge.BrowserToken, challenge.Nonce);
		Assert.False(store.TryConsume(other.BrowserToken, challenge.Nonce));
		Assert.Null(store.GetNonce("invalid"));
		var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => store.TryConsume(challenge.BrowserToken, challenge.Nonce))));
		Assert.Single(results, success => success);
		Assert.Null(store.GetNonce(challenge.BrowserToken));
		Assert.Equal(other.Nonce, store.GetNonce(other.BrowserToken));
	}

	[Fact]
	public void ChallengeExpiresAndDisabledAuthenticationCannotCreateOne()
	{
		var clock = new TestClock();
		using var store = new GoogleAuthenticationChallengeStore(Options.Create(new GoogleAuthenticationOptions { Enabled = true }), clock);
		var challenge = store.Create();
		clock.Now = challenge.ExpiresAt;
		Assert.Null(store.GetNonce(challenge.BrowserToken));
		Assert.False(store.TryConsume(challenge.BrowserToken, challenge.Nonce));
		using var disabled = new GoogleAuthenticationChallengeStore(Options.Create(new GoogleAuthenticationOptions()), clock);
		Assert.Throws<GoogleIdentityUnavailableException>(() => disabled.Create());
	}

	[Theory]
	[InlineData("valid")]
	[InlineData("issuer")]
	[InlineData("audience")]
	[InlineData("signature")]
	[InlineData("expired")]
	[InlineData("nonce")]
	[InlineData("subject")]
	[InlineData("email")]
	public async Task ValidatorChecksRealRsaSignatureIssuerAudienceExpiryAndIdentity(string scenario)
	{
		using var rsa = RSA.Create(2048);
		using var wrongRsa = RSA.Create(2048);
		var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
		var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = key.KeyId });
		using var client = new HttpClient(new MetadataHandler(jwk));
		var validator = new GoogleIdTokenValidator(new ClientFactory(client), Options.Create(new GoogleAuthenticationOptions
		{
			Enabled = true,
			ClientId = "test.apps.googleusercontent.com"
		}));
		var now = DateTime.UtcNow;
		var token = new JwtSecurityToken(
			issuer: scenario == "issuer" ? "https://attacker.test" : "https://accounts.google.com",
			audience: scenario == "audience" ? "another-client" : "test.apps.googleusercontent.com",
			claims: [new Claim("sub", scenario == "subject" ? "" : "google-123"),
				new Claim("email", scenario == "email" ? "invalid" : " USER@GMAIL.COM "),
				new Claim("email_verified", "true", ClaimValueTypes.Boolean), new Claim("nonce", "expected-nonce")],
			notBefore: now.AddMinutes(-10), expires: scenario == "expired" ? now.AddMinutes(-1) : now.AddMinutes(5),
			signingCredentials: new SigningCredentials(scenario == "signature" ? new RsaSecurityKey(wrongRsa) { KeyId = key.KeyId } : key, SecurityAlgorithms.RsaSha256));
		var identity = await validator.ValidateAsync(new JwtSecurityTokenHandler().WriteToken(token), scenario == "nonce" ? "another-nonce" : "expected-nonce");
		if (scenario == "valid")
		{
			Assert.NotNull(identity);
			Assert.Equal("google-123", identity.Subject);
			Assert.Equal("user@gmail.com", identity.Email);
			Assert.True(identity.EmailVerified);
		}
		else
			Assert.Null(identity);
	}

	[Fact]
	public async Task MetadataFailureIsUnavailableAndCancellationIsPropagated()
	{
		using var client = new HttpClient(new FailingHandler());
		var validator = new GoogleIdTokenValidator(new ClientFactory(client), Options.Create(new GoogleAuthenticationOptions { Enabled = true }));
		var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("issuer", "audience", expires: DateTime.UtcNow.AddMinutes(1)));
		await Assert.ThrowsAsync<GoogleIdentityUnavailableException>(() => validator.ValidateAsync(token, "nonce"));
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.ValidateAsync(token, "nonce", cancellation.Token));
	}

	[Theory]
	[InlineData("google.user", "2000-01-01", true)]
	[InlineData("mail@example.test", "2000-01-01", false)]
	[InlineData("google.user", "0001-01-01", false)]
	[InlineData("google.user", "2999-01-01", false)]
	public void RegistrationRequiresLoginWithoutAtSignAndValidBirthDate(string login, string birthDate, bool valid)
	{
		var request = new GoogleRegistrationRequest { Login = login, Name = "Anna", Surname = "Testowa", BirthDate = DateOnly.Parse(birthDate), IdToken = "synthetic" };
		Assert.Equal(valid, Validator.TryValidateObject(request, new ValidationContext(request), [], true));
	}

	[Theory]
	[InlineData(false, "", true)]
	[InlineData(true, "", false)]
	[InlineData(true, "invalid", false)]
	[InlineData(true, "test.apps.googleusercontent.com", true)]
	public void EnabledAuthenticationRequiresGoogleClientId(bool enabled, string clientId, bool valid)
	{
		var services = new ServiceCollection();
		services.AddUserGoogleAuthentication(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["GoogleAuthentication:Enabled"] = enabled.ToString(),
			["GoogleAuthentication:ClientId"] = clientId
		}).Build());
		using var provider = services.BuildServiceProvider();
		if (valid)
			Assert.Equal(enabled, provider.GetRequiredService<IOptions<GoogleAuthenticationOptions>>().Value.Enabled);
		else
			Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GoogleAuthenticationOptions>>().Value);
	}

	private sealed class TestClock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => client;
	}

	private sealed class MetadataHandler(JsonWebKey jwk) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var json = request.RequestUri!.AbsolutePath.EndsWith("/keys", StringComparison.Ordinal)
				? JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = jwk.Kid, n = jwk.N, e = jwk.E, use = "sig" } } })
				: JsonSerializer.Serialize(new { issuer = "https://accounts.google.com", jwks_uri = "https://accounts.google.com/keys" });
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
		}
	}

	private sealed class FailingHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			throw new HttpRequestException("Synthetic metadata outage.");
	}
}
