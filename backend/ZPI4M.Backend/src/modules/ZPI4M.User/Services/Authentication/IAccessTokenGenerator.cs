namespace ZPI4M.User.Services.Authentication;

public interface IAccessTokenGenerator
{
	(string Token, DateTime ExpiresAt) Generate(Guid userId, Guid sessionId, DateTime utcNow, DateTime sessionExpiresAt);
}
