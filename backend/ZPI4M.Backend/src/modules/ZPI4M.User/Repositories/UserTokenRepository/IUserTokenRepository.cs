using ZPI4M.User.Models;

namespace ZPI4M.User.Repositories.UserTokenRepository;

public interface IUserTokenRepository
{
	void Add(UserToken entity);
	void Remove(UserToken entity);
	Task<UserToken?> GetByTokenHashAsync(string tokenHash, UserTokenPurpose purpose, CancellationToken cancellationToken = default);
	Task<UserToken?> GetByUserAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken = default);
	Task RemoveByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
