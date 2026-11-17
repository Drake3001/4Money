using Microsoft.EntityFrameworkCore;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories.GroupRepository;

public class GroupRepository : IGroupRepository
{
	private readonly GroupingDbContext _context;

	public GroupRepository(GroupingDbContext context)
	{
		_context = context;
	}

	public Task<Group?> LockAsync(Guid groupId, CancellationToken cancellationToken = default) => _context.Groups.FromSqlInterpolated($"SELECT * FROM grouping.\"group\" WHERE id={groupId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);

	public Task<GroupBalanceSnapshot?> GetBalanceSnapshotAsync(Guid actor, Guid groupId, CancellationToken ct = default) =>
  _context.Groups.AsNoTracking().AsSingleQuery()
   .Where(g => g.Id == groupId && !g.IsArchived && g.GroupMembers.Any(m => m.FkUserId == actor && m.IsMember))
   .Select(g => new GroupBalanceSnapshot(g.Id, g.CurrencyCode, g.GroupMembers.Select(m => new GroupBalanceMember(m.FkUserId, m.IsMember, m.TotalPaid, m.TotalShare, m.TotalSettlementsSent, m.TotalSettlementsReceived)).ToList()))
   .SingleOrDefaultAsync(ct);

	public Task<Group?> GetReadAsync(Guid groupId, CancellationToken ct = default) => _context.Groups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == groupId, ct);
	public Task<Group?> GetForActiveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default) => _context.Groups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == groupId && !g.IsArchived && g.GroupMembers.Any(m => m.FkUserId == userId && m.IsMember), ct);
	public async Task<IReadOnlyList<Guid>> GetActiveExpenseGroupIdsAsync(Guid actor, CancellationToken ct = default) =>
		await _context.GroupMembers.AsNoTracking().Where(member => member.FkUserId == actor && member.IsMember && !member.FkGroup.IsArchived)
			.Select(member => member.FkGroupId).ToArrayAsync(ct);

	public async Task<PagedResult<GroupMembershipEntry>> GetMembershipPageAsync(Guid userId, GroupMembershipQuery query, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		var offset = query.GetOffset();
		var rows = _context.GroupMembers.AsNoTracking().Where(m => m.FkUserId == userId && !m.FkGroup.IsArchived);
		if (query.IsMember.HasValue) rows = rows.Where(m => m.IsMember == query.IsMember.Value);
		var count = await rows.CountAsync(ct);
		var ordered = query.SortBy switch
		{
			GroupMembershipSortBy.Name => query.Descending ? rows.OrderByDescending(m => m.FkGroup.Name) : rows.OrderBy(m => m.FkGroup.Name),
			GroupMembershipSortBy.JoinedAt => query.Descending ? rows.OrderByDescending(m => m.JoinedAt) : rows.OrderBy(m => m.JoinedAt),
			_ => throw new ArgumentOutOfRangeException(nameof(query), "Unknown membership sort field.")
		};
		var items = await ordered.ThenBy(m => m.FkGroupId).Skip(offset).Take(query.PageSize).Select(m => new GroupMembershipEntry(m.FkGroup, m.IsMember)).ToArrayAsync(ct);
		return new(items, count, query.PageNumber, query.PageSize);
	}
	public void Add(Group entity)
	{
		_context.Groups.Add(entity);
	}

	public void Remove(Group entity)
	{
		_context.Groups.Remove(entity);
	}

	public Task<Group?> GetByIdAsync(Guid groupId, CancellationToken cancellationToken = default)
	{
		return _context.Groups.SingleOrDefaultAsync(g => g.Id == groupId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid groupId, CancellationToken cancellationToken = default)
	{
		return _context.Groups.AnyAsync(g => g.Id == groupId, cancellationToken);
	}

	public async Task<PagedResult<Group>> GetPageAsync(GroupQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (query.IsAdmin.HasValue && !query.UserId.HasValue)
			throw new ArgumentException("IsAdmin requires UserId.", nameof(query));

		IQueryable<Group> groups = _context.Groups;
		if (query.UserId.HasValue)
			groups = groups.Where(g => g.GroupMembers.Any(m => m.FkUserId == query.UserId.Value &&
				(!query.IsAdmin.HasValue || (g.AdminUserId == query.UserId.Value) == query.IsAdmin.Value)));
		if (!string.IsNullOrWhiteSpace(query.Name))
		{
			var name = query.Name.Trim().ToLowerInvariant();
			groups = groups.Where(g => g.Name.ToLower().Contains(name));
		}

		IOrderedQueryable<Group> ordered;
		switch (query.SortBy)
		{
			case GroupSortBy.Name:
				if (query.Descending)
					ordered = groups.OrderByDescending(g => g.Name);
				else
					ordered = groups.OrderBy(g => g.Name);
				break;
			case GroupSortBy.MemberCount:
				if (query.Descending)
					ordered = groups.OrderByDescending(g => g.GroupMembers.Count);
				else
					ordered = groups.OrderBy(g => g.GroupMembers.Count);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(query), "Unknown group sort field.");
		}
		int offset = query.GetOffset();
		int totalCount = await groups.CountAsync(cancellationToken);
		var items = await ordered.ThenBy(g => g.Id)
			.Skip(offset)
			.Take(query.PageSize)
			.ToListAsync(cancellationToken);

		return new PagedResult<Group>(items, totalCount, query.PageNumber, query.PageSize);
	}

}
