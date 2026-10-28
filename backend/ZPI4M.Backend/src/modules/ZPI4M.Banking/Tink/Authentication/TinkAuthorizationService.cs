using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ZPI4M.Banking.Tink.Configuration;
using ZPI4M.Banking.Tink.Http;

namespace ZPI4M.Banking.Tink.Authentication;

public sealed class TinkAuthorizationService(
	TinkClientTokenProvider tokenProvider, IHttpClientFactory clientFactory, IOptions<TinkOptions> options)
{
	public async Task<string> CreateDelegatedCodeAsync(string tinkUserId, string idHint,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tinkUserId);
		ArgumentException.ThrowIfNullOrWhiteSpace(idHint);
		var token = await tokenProvider.GetTokenAsync(cancellationToken);
		using var client = clientFactory.CreateClient(TinkHttp.ClientName);
		using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/oauth/authorization-grant/delegate")
		{
			Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["user_id"] = tinkUserId,
				["actor_client_id"] = options.Value.ActorClientId,
				["id_hint"] = idHint,
				["scope"] = string.Join(',', options.Value.DelegatedScopes)
			})
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		var response = await TinkHttp.SendAsync<CodeResponse>(client, request, "delegated_authorization", cancellationToken);
		return !string.IsNullOrWhiteSpace(response.Code)
			? response.Code : throw new TinkApiException("delegated_authorization", "invalid_response");
	}

	public async Task<string> CreateUserTokenAsync(string id, bool externalId, string scope, CancellationToken cancellationToken)
	{
		var clientToken = await tokenProvider.GetTokenAsync(["authorization:grant"], cancellationToken);
		using var client = clientFactory.CreateClient(TinkHttp.ClientName);
		using var grant = new HttpRequestMessage(HttpMethod.Post, "api/v1/oauth/authorization-grant")
		{
			Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				[externalId ? "external_user_id" : "user_id"] = id,
				["scope"] = scope
			})
		};
		grant.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clientToken);
		var code = await TinkHttp.SendAsync<AuthorizationCode>(client, grant, "user_authorization", cancellationToken);
		if (string.IsNullOrWhiteSpace(code.Code))
			throw new TinkApiException("user_authorization", "invalid_response");
		using var exchange = new HttpRequestMessage(HttpMethod.Post, "api/v1/oauth/token")
		{
			Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["client_id"] = options.Value.ClientId,
				["client_secret"] = options.Value.ClientSecret,
				["grant_type"] = "authorization_code",
				["code"] = code.Code
			})
		};
		var token = await TinkHttp.SendAsync<UserToken>(client, exchange, "user_token", cancellationToken);
		if (string.IsNullOrWhiteSpace(token.AccessToken))
			throw new TinkApiException("user_token", "invalid_response");
		return token.AccessToken;
	}


	private sealed record AuthorizationCode([property: JsonPropertyName("code")] string? Code);
	private sealed record UserToken([property: JsonPropertyName("access_token")] string? AccessToken);

	private sealed record CodeResponse([property: JsonPropertyName("code")] string? Code);
}
