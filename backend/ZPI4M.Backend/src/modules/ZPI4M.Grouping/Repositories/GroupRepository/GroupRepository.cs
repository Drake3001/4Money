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
				(!query.IsAdmin.HasValue || m.IsAdmin == query.IsAdmin.Value)));
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
			case GroupSortBy.WeeklyLimit:
				if (query.Descending)
					ordered = groups.OrderByDescending(g => g.WeeklyLimit);
				else
					ordered = groups.OrderBy(g => g.WeeklyLimit);
				break;
			case GroupSortBy.MonthlyLimit:
				if (query.Descending)
					ordered = groups.OrderByDescending(g => g.MonthlyLimit);
				else
					ordered = groups.OrderBy(g => g.MonthlyLimit);
				break;
			case GroupSortBy.YearLimit:
				if (query.Descending)
					ordered = groups.OrderByDescending(g => g.YearLimit);
				else
					ordered = groups.OrderBy(g => g.YearLimit);
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
