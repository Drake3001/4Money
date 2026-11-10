namespace ZPI4M.User.Services.GoogleAuthentication;

public interface IGoogleAuthenticationChallengeStore
{
	GoogleAuthenticationChallenge Create();
	string? GetNonce(string browserToken);
	bool TryConsume(string browserToken, string nonce);
}
