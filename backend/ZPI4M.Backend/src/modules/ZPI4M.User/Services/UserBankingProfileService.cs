using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ZPI4M.User.Database;

namespace ZPI4M.User.Services;

public sealed class UserBankingProfileService(UserDbContext database) : IUserBankingProfileService
{
	public async Task<UserBankingProfile?> GetAsync(Guid userId, CancellationToken cancellationToken)
	{
		var user = await database.Users.AsNoTracking().Where(x => x.Id == userId)
		 .Select(x => new { x.Id, x.IsActive, x.TinkUserId, x.Name, x.Surname, x.Login })
		 .SingleOrDefaultAsync(cancellationToken);
		if (user is null)
			return null;
		var hint = string.Join(' ', new[] { user.Name, user.Surname }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
		return new(user.Id, user.IsActive, user.TinkUserId, string.IsNullOrWhiteSpace(hint) ? user.Login : hint);
	}

	public async Task<UserBankingProfile?> EnsureTinkUserAsync(Guid userId, Func<CancellationToken, Task<string>> createOrRecover,
	 CancellationToken cancellationToken)
	{
		await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
		// Only provisioning takes this advisory lock. Login/profile writes never wait for Tink.
		var lockId = BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"4money/tink-user/{userId:N}")));
		await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", cancellationToken);
		var profile = await GetAsync(userId, cancellationToken);
		if (profile is null)
			return null;
		if (profile.IsActive && string.IsNullOrWhiteSpace(profile.TinkUserId))
		{
			var tinkUserId = await createOrRecover(cancellationToken);
			var updated = await database.Users.Where(x => x.Id == userId && x.IsActive
			 && (x.TinkUserId == null || x.TinkUserId.Trim() == ""))
			 .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TinkUserId, tinkUserId), cancellationToken);
			// Respect deactivation or an explicit mapping change made while the remote call was in flight.
			profile = updated == 1 ? profile with { TinkUserId = tinkUserId } : await GetAsync(userId, cancellationToken);
		}
		await transaction.CommitAsync(cancellationToken);
		return profile;
	}
}
