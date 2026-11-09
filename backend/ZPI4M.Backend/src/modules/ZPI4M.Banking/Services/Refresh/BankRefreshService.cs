using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories.BankRefreshRepository;
using ZPI4M.User.Services.BankingProfile;

namespace ZPI4M.Banking.Services.Refresh;

public sealed class BankRefreshService(BankRefreshRepository repository, IUserBankingProfileService users)
{
	public async Task<BankRefresh?> StartAsync(Guid userId, CancellationToken ct)
	{
		var user = await users.GetAsync(userId, ct);
		if (user is null || !user.IsActive) throw new RefreshIdentityException();
		if (string.IsNullOrWhiteSpace(user.TinkUserId)) return null;
		return await repository.EnqueueAsync(userId, user.TinkUserId, ct);
	}

	public Task<BankRefresh?> GetAsync(Guid userId, Guid id, CancellationToken ct) => repository.GetAsync(userId, id, ct);
}
