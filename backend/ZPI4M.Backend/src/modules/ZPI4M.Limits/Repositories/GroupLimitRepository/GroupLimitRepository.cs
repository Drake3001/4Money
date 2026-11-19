using Microsoft.EntityFrameworkCore;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.Repositories.GroupLimitRepository;

public sealed class GroupLimitRepository(LimitsDbContext db) : IGroupLimitRepository
{
	public Task<GroupLimit?> GetAsync(Guid groupId, Guid limitId, bool tracking, CancellationToken token = default) => (tracking ? db.GroupLimits : db.GroupLimits.AsNoTracking()).SingleOrDefaultAsync(l => l.GroupId == groupId && l.Id == limitId, token);
	public Task<GroupLimit[]> GetEnabledScopeAsync(Guid groupId, Guid? categoryId, string currency, CancellationToken token = default) => db.GroupLimits.Where(l => l.GroupId == groupId && l.CategoryId == categoryId && l.CurrencyCode == currency && l.IsEnabled).ToArrayAsync(token);
	public async Task<PagedResult<GroupLimit>> GetPageAsync(Guid groupId, LimitQuery query, CancellationToken token = default)
	{
		var rows = db.GroupLimits.AsNoTracking().Where(l => l.GroupId == groupId);
		if (!query.IncludeDisabled)
			rows = rows.Where(l => l.IsEnabled);
		if (query.CategoryId is Guid category)
			rows = rows.Where(l => l.CategoryId == category);
		if (query.CurrencyCode is not null)
			rows = rows.Where(l => l.CurrencyCode == query.CurrencyCode);
		if (query.Period is LimitPeriod period)
			rows = rows.Where(l => l.Period == period);
		var count = await rows.CountAsync(token);
		var ordered = query.Descending ? rows.OrderByDescending(l => l.SetDate).ThenByDescending(l => l.Id) : rows.OrderBy(l => l.SetDate).ThenBy(l => l.Id);
		return new(await ordered.Skip(query.GetOffset()).Take(query.PageSize).ToArrayAsync(token), count, query.PageNumber, query.PageSize);
	}

	public void Add(GroupLimit value) => db.GroupLimits.Add(value);
}
