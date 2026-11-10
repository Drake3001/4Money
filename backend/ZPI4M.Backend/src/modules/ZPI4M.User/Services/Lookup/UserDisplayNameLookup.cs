using Microsoft.EntityFrameworkCore;
using ZPI4M.User.Database;

namespace ZPI4M.User.Services.Lookup;

public sealed class UserDisplayNameLookup(UserDbContext database) : IUserDisplayNameLookup
{
	public async Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
	{
		var requested = ids.Distinct().ToArray();
		var users = await database.Users.AsNoTracking().Where(user => requested.Contains(user.Id))
			.Select(user => new { user.Id, user.Name, user.Surname, user.Login }).ToArrayAsync(cancellationToken);
		return users.ToDictionary(user => user.Id, user =>
			string.IsNullOrWhiteSpace(user.Name + user.Surname) ? user.Login : $"{user.Name} {user.Surname}".Trim());
	}
}
