using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ZPI4M.Banking.Tink.Authentication;
using ZPI4M.Banking.Tink.Configuration;
using ZPI4M.Banking.Tink.Http;
using ZPI4M.Banking.Tink.Link;

namespace ZPI4M.Banking.Tink;

public sealed class TinkConnectionGateway(TinkClientTokenProvider tokens, TinkAuthorizationService authorization,
 TinkLinkUrlBuilder links, IHttpClientFactory clients, IOptions<TinkOptions> options) : ITinkConnectionGateway
{
	public async Task<string> CreateOrRecoverUserAsync(Guid userId, CancellationToken cancellationToken)
	{
		EnsureEnabled();
		var externalId = $"4money-{userId:N}";
		var clientToken = await tokens.GetTokenAsync(["user:create"], cancellationToken);
		using var client = clients.CreateClient(TinkHttp.ClientName);
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/user/create")
		{
			Content = JsonContent.Create(new { external_user_id = externalId, market = options.Value.Market, locale = options.Value.Locale })
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clientToken);
		try
		{
			var created = await TinkHttp.SendAsync<CreatedUser>(client, request, "create_user", cancellationToken);
			if (!ValidId(created.UserId) || created.ExternalUserId != externalId)
				throw new TinkApiException("create_user", "invalid_response");
			return created.UserId!;
		}
		catch (TinkApiException exception) when (exception.UpstreamStatus == HttpStatusCode.Conflict
		 || exception.ErrorCode is "timeout" or "network_error" or "invalid_response")
		{
			// Stable, unique external_user_id makes remote creation recoverable after a lost response/local rollback.
			// Never generate another external ID or replay this POST automatically.
			var token = await authorization.CreateUserTokenAsync(externalId, true, "user:read", cancellationToken);
			using var read = new HttpRequestMessage(HttpMethod.Get, "api/v1/user");
			read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			var user = await TinkHttp.SendAsync<UserIdentity>(client, read, "recover_user", cancellationToken);
			if (!ValidId(user.Id) || user.ExternalUserId != externalId)
				throw new TinkApiException("recover_user", "invalid_response");
			return user.Id!;
		}
	}

	public async Task<string> CreateLinkAsync(string tinkUserId, string idHint, string state, CancellationToken cancellationToken)
	{
		EnsureEnabled();
		var code = await authorization.CreateDelegatedCodeAsync(tinkUserId, idHint, cancellationToken);
		return links.Build(code, state);
	}

	public async Task<TinkVerifiedConnection?> VerifyConnectionAsync(string tinkUserId, string credentialsId, CancellationToken cancellationToken)
	{
		EnsureEnabled();
		var token = await authorization.CreateUserTokenAsync(tinkUserId, false, "credentials:read", cancellationToken);
		using var client = clients.CreateClient(TinkHttp.ClientName);
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/credentials/" + Uri.EscapeDataString(credentialsId));
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		Credential credential;
		try
		{
			credential = await TinkHttp.SendAsync<Credential>(client, request, "verify_connection", cancellationToken);
		}
		catch (TinkApiException exception) when (exception.UpstreamStatus == HttpStatusCode.NotFound)
		{
			return null;
		}
		if (credential.Id != credentialsId || credential.UserId != tinkUserId || !ValidId(credential.ProviderName, 256))
			return null;
		if (credential.Status != "UPDATED")
			throw new TinkApiException("verify_connection", "connection_not_ready");
		return new(credentialsId, credential.ProviderName!);
	}

	public async Task<IReadOnlyList<TinkProviderConsent>> GetConsentsAsync(string tinkUserId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tinkUserId);
		EnsureEnabled();
		var token = await authorization.CreateUserTokenAsync(tinkUserId, false, "provider-consents:read", cancellationToken);
		using var client = clients.CreateClient(TinkHttp.ClientName);
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/provider-consents");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		var response = await TinkHttp.SendAsync<ConsentResponse>(client, request, "provider_consents", cancellationToken);
		if (response.ProviderConsents is null)
			throw new TinkApiException("provider_consents", "invalid_response");

		var result = new List<TinkProviderConsent>();
		foreach (var consent in response.ProviderConsents)
		{
			if (consent is null || !ValidId(consent.CredentialsId) || string.IsNullOrWhiteSpace(consent.Status))
				throw new TinkApiException("provider_consents", "invalid_response");
			DateTimeOffset? expiresAt = null;
			if (consent.SessionExpiryDate is { } timestamp)
			{
				try
				{
					expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
				}
				catch (ArgumentOutOfRangeException)
				{
					throw new TinkApiException("provider_consents", "invalid_response");
				}
			}
			result.Add(new(consent.CredentialsId!, consent.Status, expiresAt, consent.SessionExtendable));
		}
		return result;
	}


	private void EnsureEnabled()
	{
		if (!options.Value.ContinuousAccessVerified)
			throw new TinkApiException("configuration", "continuous_access_unverified");
	}

	private static bool ValidId(string? value, int maxLength = 128)
	 => !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;

	private sealed record ConsentResponse([property: JsonPropertyName("providerConsents")] ProviderConsent?[]? ProviderConsents);
	private sealed record ProviderConsent([property: JsonPropertyName("credentialsId")] string? CredentialsId,
	 [property: JsonPropertyName("status")] string? Status,
	 [property: JsonPropertyName("sessionExpiryDate")] long? SessionExpiryDate,
	 [property: JsonPropertyName("sessionExtendable")] bool? SessionExtendable);
	private sealed record CreatedUser([property: JsonPropertyName("user_id")] string? UserId,
	 [property: JsonPropertyName("external_user_id")] string? ExternalUserId);
	private sealed record UserIdentity([property: JsonPropertyName("id")] string? Id,
	 [property: JsonPropertyName("externalUserId")] string? ExternalUserId);
	private sealed record Credential([property: JsonPropertyName("id")] string? Id,
	 [property: JsonPropertyName("userId")] string? UserId,
	 [property: JsonPropertyName("providerName")] string? ProviderName,
	 [property: JsonPropertyName("status")] string? Status);
}
