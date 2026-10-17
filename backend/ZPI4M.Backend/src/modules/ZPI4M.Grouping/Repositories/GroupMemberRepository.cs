using Microsoft.EntityFrameworkCore;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories;

public class GroupMemberRepository : IGroupMemberRepository
{
	private readonly GroupingDbContext _context;

	public GroupMemberRepository(GroupingDbContext context)
	{
		_context = context;
	}

	public void Add(GroupMember entity)
	{
		_context.GroupMembers.Add(entity);
	}

	public void Remove(GroupMember entity)
	{
		_context.GroupMembers.Remove(entity);
	}

	public Task<GroupMember?> GetAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
	{
		return _context.GroupMembers.SingleOrDefaultAsync(m => m.FkGroupId == groupId && m.FkUserId == userId, cancellationToken);
	}

	public Task<bool> IsMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
	{
		return _context.GroupMembers.AnyAsync(m => m.FkGroupId == groupId && m.FkUserId == userId, cancellationToken);
	}

	public Task<bool> IsAdminAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
	{
		return _context.GroupMembers.AnyAsync(m => m.FkGroupId == groupId && m.FkUserId == userId && m.IsAdmin, cancellationToken);
	}

	public Task<int> CountAsync(Guid groupId, bool? isAdmin = null, CancellationToken cancellationToken = default)
	{
		return _context.GroupMembers.CountAsync(m => m.FkGroupId == groupId && (!isAdmin.HasValue || m.IsAdmin == isAdmin.Value), cancellationToken);
	}

	public async Task<PagedResult<GroupMember>> GetPageAsync(Guid groupId, GroupMemberQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		var members = _context.GroupMembers.Where(m => m.FkGroupId == groupId);
		if (query.IsAdmin.HasValue)
			members = members.Where(m => m.IsAdmin == query.IsAdmin.Value);
		IOrderedQueryable<GroupMember> ordered;
		switch (query.SortBy)
		{
			case GroupMemberSortBy.UserId:
				if (query.Descending)
					ordered = members.OrderByDescending(m => m.FkUserId);
				else
					ordered = members.OrderBy(m => m.FkUserId);
				break;
			case GroupMemberSortBy.IsAdmin:
				if (query.Descending)
					ordered = members.OrderByDescending(m => m.IsAdmin);
				else
					ordered = members.OrderBy(m => m.IsAdmin);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(query), "Unknown sort field.");
		}
		int offset = query.GetOffset();
		int totalCount = await members.CountAsync(cancellationToken);
		var items = await ordered.ThenBy(m => m.FkUserId)
			.Skip(offset)
			.Take(query.PageSize)
			.ToListAsync(cancellationToken);

		return new PagedResult<GroupMember>(items, totalCount, query.PageNumber, query.PageSize);
	}
}
