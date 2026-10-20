using ZPI4M.Shared.Pagination;

namespace ZPI4M.User.Repositories.UserRepository;

public interface IUserRepository
{
	void Add(Models.User entity);
	void Remove(Models.User entity);
	Task<Models.User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<PagedResult<Models.User>> GetPageAsync(UserQuery query, CancellationToken cancellationToken = default);
	Task<Models.User?> GetByLoginAsync(string login, CancellationToken cancellationToken = default);
	Task<bool> ExistsByLoginAsync(string login, CancellationToken cancellationToken = default);
	Task<Models.User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
	Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
	Task<bool> TryConfirmEmailAsync(Guid userId, string email, CancellationToken cancellationToken = default);
	Task<Models.User?> GetByTinkUserIdAsync(string tinkUserId, CancellationToken cancellationToken = default);
}
