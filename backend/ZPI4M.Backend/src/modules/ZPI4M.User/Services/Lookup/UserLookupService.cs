using Microsoft.EntityFrameworkCore;
using ZPI4M.User.Database;

namespace ZPI4M.User.Services.Lookup;

public sealed class UserLookupService(UserDbContext database) : IUserLookupService
{
	public async Task<IReadOnlyCollection<Guid>> GetActiveUserIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(ids);
		cancellationToken.ThrowIfCancellationRequested();
		if (ids.Count == 0)
			return Array.Empty<Guid>();

		var requestedIds = ids.Distinct().ToArray();
		return await database.Users.AsNoTracking()
			.Where(user => user.IsActive && requestedIds.Contains(user.Id))
			.Select(user => user.Id)
			.ToArrayAsync(cancellationToken);
	}
}
