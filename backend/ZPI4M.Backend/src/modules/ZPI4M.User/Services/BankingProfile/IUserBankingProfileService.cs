namespace ZPI4M.User.Services.BankingProfile;

public sealed record UserBankingProfile(Guid Id, bool IsActive, string? TinkUserId, string IdHint);

public interface IUserBankingProfileService
{
	Task<UserBankingProfile?> GetAsync(Guid userId, CancellationToken cancellationToken);
	// Serializes mapping initialization across backend instances without locking login/profile writes.
	Task<UserBankingProfile?> EnsureTinkUserAsync(Guid userId, Func<CancellationToken, Task<string>> createOrRecover,
	 CancellationToken cancellationToken);
}
