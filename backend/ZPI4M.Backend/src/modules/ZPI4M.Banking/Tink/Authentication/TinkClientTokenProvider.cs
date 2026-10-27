using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ZPI4M.Banking.Tink.Configuration;
using ZPI4M.Banking.Tink.Http;

namespace ZPI4M.Banking.Tink.Authentication;

public sealed class TinkClientTokenProvider(
	IMemoryCache cache, IHttpClientFactory clientFactory, IOptions<TinkOptions> options,
	TimeProvider timeProvider) : IDisposable
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private TinkOptions Settings => options.Value;
	private readonly record struct TokenCacheKey(string ApiUrl, string ClientId, string Scopes);
	private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);

	public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
  => GetTokenAsync(Settings.ClientScopes, cancellationToken);

	public async Task<string> GetTokenAsync(IEnumerable<string> requestedScopes, CancellationToken cancellationToken = default)
	{
		var scopes = string.Join(',', requestedScopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
		var key = new TokenCacheKey(Settings.ApiBaseUrl, Settings.ClientId, scopes);
		if (TryGetToken(key, out var token))
		{
			return token!;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (TryGetToken(key, out token))
			{
				return token!;
			}

			var startedAt = timeProvider.GetUtcNow();
			using var client = clientFactory.CreateClient(TinkHttp.ClientName);
			using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/oauth/token")
			{
				Content = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["client_id"] = Settings.ClientId,
					["client_secret"] = Settings.ClientSecret,
					["grant_type"] = "client_credentials",
					["scope"] = scopes
				})
			};
			var response = await TinkHttp.SendAsync<TokenResponse>(client, request, "client_token", cancellationToken);
			if (string.IsNullOrWhiteSpace(response.AccessToken) || response.ExpiresIn <= 0
				|| response.ExpiresIn > TimeSpan.MaxValue.TotalSeconds / 2)
			{
				throw new TinkApiException("client_token", "invalid_response");
			}

			var lifetime = response.ExpiresIn - Math.Min(60, response.ExpiresIn * 0.1);
			// Reject unreasonable values before DateTimeOffset arithmetic.
			if (lifetime >= (DateTimeOffset.MaxValue - startedAt).TotalSeconds)
			{
				throw new TinkApiException("client_token", "invalid_response");
			}
			var expiresAt = startedAt.AddSeconds(lifetime);
			if (expiresAt <= timeProvider.GetUtcNow())
			{
				throw new TinkApiException("client_token", "invalid_response");
			}

			cache.Set(key, new CachedToken(response.AccessToken, expiresAt), expiresAt);
			return response.AccessToken;
		}
		finally
		{
			_gate.Release();
		}
	}

	private bool TryGetToken(TokenCacheKey key, out string? token)
	{
		if (cache.TryGetValue<CachedToken>(key, out var cached)
			&& cached is not null && cached.ExpiresAt > timeProvider.GetUtcNow())
		{
			token = cached.Value;
			return true;
		}
		token = null;
		return false;
	}

	public void Dispose() => _gate.Dispose();

	private sealed record TokenResponse(
		[property: JsonPropertyName("access_token")] string? AccessToken,
		[property: JsonPropertyName("expires_in")] long ExpiresIn);
}
