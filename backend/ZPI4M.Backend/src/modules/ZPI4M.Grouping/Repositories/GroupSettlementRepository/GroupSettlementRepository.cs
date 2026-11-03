using Microsoft.EntityFrameworkCore;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;
namespace ZPI4M.Grouping.Repositories.GroupSettlementRepository;

public sealed class GroupSettlementRepository(GroupingDbContext db) : IGroupSettlementRepository
{
	public void Add(GroupSettlement value) => db.GroupSettlements.Add(value);
	public void Remove(GroupSettlement value) => db.GroupSettlements.Remove(value);
	public Task<GroupSettlement?> GetAsync(Guid groupId, Guid id, bool tracking, CancellationToken ct) => (tracking ? db.GroupSettlements : db.GroupSettlements.AsNoTracking()).SingleOrDefaultAsync(s => s.GroupId == groupId && s.Id == id, ct);
	public async Task<PagedResult<GroupSettlement>> GetPageAsync(Guid groupId, GroupSettlementQuery query, CancellationToken ct)
	{
		var offset = query.GetOffset(); var rows = db.GroupSettlements.AsNoTracking().Where(s => s.GroupId == groupId);
		if (query.FromUserId.HasValue) rows = rows.Where(s => s.FromUserId == query.FromUserId.Value);
		if (query.ToUserId.HasValue) rows = rows.Where(s => s.ToUserId == query.ToUserId.Value);
		if (query.Method.HasValue) rows = rows.Where(s => s.Method == query.Method.Value);
		if (query.DateFrom.HasValue) rows = rows.Where(s => s.SettledAt >= query.DateFrom.Value.UtcDateTime);
		if (query.DateTo.HasValue) rows = rows.Where(s => s.SettledAt <= query.DateTo.Value.UtcDateTime);
		var count = await rows.CountAsync(ct);
		var order = query.Descending ? rows.OrderByDescending(s => s.SettledAt) : rows.OrderBy(s => s.SettledAt);
		return new(await order.ThenBy(s => s.Id).Skip(offset).Take(query.PageSize).ToArrayAsync(ct), count, query.PageNumber, query.PageSize);
	}
}
