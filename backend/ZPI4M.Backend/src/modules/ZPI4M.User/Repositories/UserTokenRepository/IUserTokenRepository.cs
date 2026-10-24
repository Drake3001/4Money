using ZPI4M.User.Models;

namespace ZPI4M.User.Repositories.UserTokenRepository;

public interface IUserTokenRepository
{
	void Add(UserToken entity);
	void Remove(UserToken entity);
	Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, DateTime utcNow, CancellationToken cancellationToken = default);
	Task RemoveSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
	Task<bool> TryConsumeAsync(Guid tokenId, string tokenHash, UserTokenPurpose purpose, DateTime utcNow, CancellationToken cancellationToken = default);
	Task<UserToken?> GetByTokenHashAsync(string tokenHash, UserTokenPurpose purpose, CancellationToken cancellationToken = default);
	Task<UserToken?> GetByUserAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken = default);
	Task RemoveByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
