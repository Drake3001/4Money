using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ZPI4M.User.Integrations.Google;

public sealed class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
	internal const string HttpClientName = "User.GoogleIdentity";
	private readonly GoogleAuthenticationOptions _options;
	private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;

	public GoogleIdTokenValidator(IHttpClientFactory clients, IOptions<GoogleAuthenticationOptions> options)
	{
		_options = options.Value;
		_configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
			"https://accounts.google.com/.well-known/openid-configuration",
			new OpenIdConnectConfigurationRetriever(),
			new HttpDocumentRetriever(clients.CreateClient(HttpClientName)) { RequireHttps = true });
	}

	public async Task<GoogleIdentity?> ValidateAsync(string idToken, string expectedNonce, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!_options.Enabled)
			throw new GoogleIdentityUnavailableException("Google authentication is disabled.");
		if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 16384 || string.IsNullOrWhiteSpace(expectedNonce))
			return null;

		var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 16384 };
		if (!handler.CanReadToken(idToken))
			return null;
		var configuration = await GetConfigurationAsync(cancellationToken);
		try
		{
			var principal = handler.ValidateToken(idToken, new TokenValidationParameters
			{
				ValidateIssuer = true,
				ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
				ValidateAudience = true,
				ValidAudience = _options.ClientId,
				ValidateIssuerSigningKey = true,
				IssuerSigningKeys = configuration.SigningKeys,
				RequireSignedTokens = true,
				ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
				ValidateLifetime = true,
				RequireExpirationTime = true,
				ClockSkew = TimeSpan.Zero
			}, out _);
			var subject = principal.FindFirst("sub")?.Value;
			var email = principal.FindFirst("email")?.Value;
			if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) || email.Length > 254 ||
				!new EmailAddressAttribute().IsValid(email) ||
				!string.Equals(principal.FindFirst("nonce")?.Value, expectedNonce, StringComparison.Ordinal))
				return null;

			bool.TryParse(principal.FindFirst("email_verified")?.Value, out var emailVerified);
			return new GoogleIdentity(subject, email.Trim().ToLowerInvariant(), emailVerified,
				principal.FindFirst("given_name")?.Value, principal.FindFirst("family_name")?.Value,
				principal.FindFirst("hd")?.Value);
		}
		catch (SecurityTokenSignatureKeyNotFoundException)
		{
			_configuration.RequestRefresh();
			return null;
		}
		catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
		{
			return null;
		}
	}

	private async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await _configuration.GetConfigurationAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException or OperationCanceledException)
		{
			throw new GoogleIdentityUnavailableException("Google identity verification is temporarily unavailable.", exception);
		}
	}
}
