namespace ZPI4M.Banking.Tink.Configuration;

public sealed class TinkOptions
{
	// Explicit deployment gate: confirm product, market, scopes and callback in Tink Console.
	public bool ContinuousAccessVerified { get; set; }
	public const string SectionName = "Tink";
	public string ClientId { get; set; } = "";
	public string ClientSecret { get; set; } = "";
	public string ActorClientId { get; set; } = "df05e4b379934cd09963197cc855bfe9";
	public string ApiBaseUrl { get; set; } = "https://api.tink.com/";
	public string LinkUrl { get; set; } = "https://link.tink.com/1.0/transactions/connect-accounts";
	public string RedirectUri { get; set; } = "";
	public string Market { get; set; } = "PL";
	public string Locale { get; set; } = "en_US";
	public string[] ClientScopes { get; set; } = ["authorization:grant"];
	public string[] DelegatedScopes { get; set; } =
	[
		"accounts:read", "authorization:grant", "authorization:read", "credentials:read",
		"credentials:refresh", "credentials:write", "providers:read", "user:read"
	];
	public string[] RefreshableItems { get; set; } =
	[
		"CHECKING_ACCOUNTS", "CHECKING_TRANSACTIONS", "SAVING_ACCOUNTS",
		"SAVING_TRANSACTIONS", "CREDITCARD_ACCOUNTS", "CREDITCARD_TRANSACTIONS"
	];

	public bool IsValid()
	{
		string[] allowedItems =
		[
			"CHECKING_ACCOUNTS", "CHECKING_TRANSACTIONS", "SAVING_ACCOUNTS",
			"SAVING_TRANSACTIONS", "CREDITCARD_ACCOUNTS", "CREDITCARD_TRANSACTIONS"
		];
		return !string.IsNullOrWhiteSpace(ClientId)
			&& !string.IsNullOrWhiteSpace(ClientSecret)
			&& !string.IsNullOrWhiteSpace(ActorClientId)
			&& !string.Equals(ClientId, ActorClientId, StringComparison.Ordinal)
			&& IsHttpsUrl(ApiBaseUrl) && IsHttpsUrl(LinkUrl)
			&& Uri.TryCreate(RedirectUri, UriKind.Absolute, out var redirect)
			&& string.IsNullOrEmpty(redirect.Fragment)
			&& !string.IsNullOrWhiteSpace(Market) && !string.IsNullOrWhiteSpace(Locale)
			&& ClientScopes is { Length: > 0 } && ClientScopes.All(IsScope)
			&& ClientScopes.Contains("authorization:grant", StringComparer.Ordinal)
			&& DelegatedScopes is { Length: > 0 } && DelegatedScopes.All(IsScope)
			&& new TinkOptions().DelegatedScopes.All(scope => DelegatedScopes.Contains(scope, StringComparer.Ordinal))
			&& RefreshableItems is { Length: > 0 }
			&& RefreshableItems.All(item => allowedItems.Contains(item, StringComparer.Ordinal));
	}

	private static bool IsScope(string scope) => !string.IsNullOrWhiteSpace(scope)
		&& !scope.Any(char.IsWhiteSpace) && !scope.Contains(',');

	private static bool IsHttpsUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
		&& uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
		&& string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
