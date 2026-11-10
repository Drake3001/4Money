using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ZPI4M.User.Integrations.Google;

namespace ZPI4M.User.Services.GoogleAuthentication;

public sealed class GoogleAuthenticationChallengeStore : IGoogleAuthenticationChallengeStore, IDisposable
{
	private readonly GoogleAuthenticationOptions _options;
	private readonly TimeProvider _time;
	private readonly MemoryCache _cache;

	public GoogleAuthenticationChallengeStore(IOptions<GoogleAuthenticationOptions> options, TimeProvider time)
	{
		_options = options.Value;
		_time = time;
		_cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = _options.MaxPendingChallenges });
	}

	public GoogleAuthenticationChallenge Create()
	{
		if (!_options.Enabled)
			throw new GoogleIdentityUnavailableException("Google authentication is disabled.");
		var browserToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
		var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
		var lifetime = TimeSpan.FromMinutes(_options.ChallengeLifetimeMinutes);
		var expiresAt = _time.GetUtcNow().Add(lifetime);
		_cache.Set(browserToken, new Entry(nonce, expiresAt), new MemoryCacheEntryOptions
		{
			Size = 1,
			AbsoluteExpirationRelativeToNow = lifetime
		});
		if (!_cache.TryGetValue(browserToken, out Entry? _))
			throw new GoogleIdentityUnavailableException("Google authentication challenge capacity was reached.");
		return new GoogleAuthenticationChallenge(browserToken, nonce, expiresAt);
	}

	public string? GetNonce(string browserToken) => GetEntry(browserToken)?.Nonce;

	public bool TryConsume(string browserToken, string nonce)
	{
		var entry = GetEntry(browserToken);
		if (entry is null || !string.Equals(entry.Nonce, nonce, StringComparison.Ordinal) || !entry.TryConsume())
			return false;
		_cache.Remove(browserToken);
		return true;
	}

	private Entry? GetEntry(string browserToken)
	{
		if (!_options.Enabled || string.IsNullOrEmpty(browserToken) || browserToken.Length != 43 ||
			!_cache.TryGetValue(browserToken, out Entry? entry) || entry is null)
			return null;
		if (entry.ExpiresAt > _time.GetUtcNow() && !entry.IsConsumed)
			return entry;
		_cache.Remove(browserToken);
		return null;
	}

	public void Dispose() => _cache.Dispose();

	private sealed class Entry(string nonce, DateTimeOffset expiresAt)
	{
		private int _consumed;
		public string Nonce { get; } = nonce;
		public DateTimeOffset ExpiresAt { get; } = expiresAt;
		public bool IsConsumed => Volatile.Read(ref _consumed) != 0;
		public bool TryConsume() => Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;
	}
}
