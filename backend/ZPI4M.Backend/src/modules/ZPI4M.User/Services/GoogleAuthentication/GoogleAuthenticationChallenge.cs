namespace ZPI4M.User.Services.GoogleAuthentication;

public sealed record GoogleAuthenticationChallenge(
	string BrowserToken,
	string Nonce,
	DateTimeOffset ExpiresAt);
