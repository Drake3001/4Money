using Microsoft.EntityFrameworkCore;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories.GroupInvitationRepository;

public sealed class GroupInvitationRepository(GroupingDbContext db) : IGroupInvitationRepository
{
	public Task<GroupInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
		return db.GroupInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);
	}

	public async Task<PagedResult<GroupInvitation>> GetPageAsync(Guid groupId, GroupInvitationQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		var offset = query.GetOffset();
		var rows = db.GroupInvitations.AsNoTracking().Where(i => i.GroupId == groupId);
		if (query.IsActive.HasValue)
			rows = rows.Where(i => i.IsActive == query.IsActive.Value);
		var count = await rows.CountAsync(cancellationToken);
		var ordered = query.Descending ? rows.OrderByDescending(i => i.ExpiresAt) : rows.OrderBy(i => i.ExpiresAt);
		var items = await ordered.ThenBy(i => i.Id).Skip(offset).Take(query.PageSize).ToArrayAsync(cancellationToken);
		return new(items, count, query.PageNumber, query.PageSize);
	}

	public Task<GroupInvitation?> LockAsync(Guid id, CancellationToken ct) => db.GroupInvitations.FromSqlInterpolated($"SELECT * FROM grouping.group_invitation WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(ct);
	public void Add(GroupInvitation invitation) => db.GroupInvitations.Add(invitation);
}
