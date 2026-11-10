namespace ZPI4M.User.Integrations.Google;

public interface IGoogleIdTokenValidator
{
	Task<GoogleIdentity?> ValidateAsync(string idToken, string expectedNonce, CancellationToken cancellationToken = default);
}
