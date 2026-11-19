using Microsoft.EntityFrameworkCore;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.Repositories.UserLimitRepository;

public sealed class UserLimitRepository(LimitsDbContext db) : IUserLimitRepository
{
	public Task<UserLimit?> GetAsync(Guid userId, Guid limitId, bool tracking, CancellationToken token = default) => (tracking ? db.UserLimits : db.UserLimits.AsNoTracking()).SingleOrDefaultAsync(l => l.UserId == userId && l.Id == limitId, token);
	public Task<UserLimit[]> GetEnabledScopeAsync(Guid userId, Guid? categoryId, string currency, CancellationToken token = default) => db.UserLimits.Where(l => l.UserId == userId && l.CategoryId == categoryId && l.CurrencyCode == currency && l.IsEnabled).ToArrayAsync(token);
	public async Task<PagedResult<UserLimit>> GetPageAsync(Guid userId, LimitQuery query, CancellationToken token = default)
	{
		var rows = db.UserLimits.AsNoTracking().Where(l => l.UserId == userId);
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

	public void Add(UserLimit value) => db.UserLimits.Add(value);
}
