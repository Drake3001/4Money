using Microsoft.EntityFrameworkCore;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Repositories.LimitSweepRepository;

public sealed class LimitSweepRepository(LimitsDbContext db) : ILimitSweepRepository
{
	public async Task<ILimitDefinition?> GetAsync(LimitSweepCandidate candidate, CancellationToken token) => candidate.IsGroup ? await db.GroupLimits.SingleOrDefaultAsync(l => l.Id == candidate.Id && l.GroupId == candidate.OwnerId, token) : await db.UserLimits.SingleOrDefaultAsync(l => l.Id == candidate.Id && l.UserId == candidate.OwnerId, token);
	public async Task<LimitSweepCandidate[]> PageAsync(bool group, Guid after, int take, DateOnly today, CancellationToken token)
	{
		if (group)
			return await db.GroupLimits.AsNoTracking().Where(l => l.IsEnabled && (l.Period != LimitPeriod.Custom || (l.StartDate <= today && l.EndDate >= today)) && l.Id.CompareTo(after) > 0).OrderBy(l => l.Id).Take(take).Select(l => new LimitSweepCandidate(true, l.Id, l.GroupId)).ToArrayAsync(token);
		var users = await db.UserLimits.AsNoTracking().Where(l => l.IsEnabled && (l.Period != LimitPeriod.Custom || (l.StartDate <= today && l.EndDate >= today)) && l.Id.CompareTo(after) > 0).OrderBy(l => l.Id).Take(take).Select(l => new LimitSweepCandidate(false, l.Id, l.UserId)).ToArrayAsync(token);
		if (users.Length == take)
			return users;
		var groups = await db.GroupLimits.AsNoTracking().Where(l => l.IsEnabled && (l.Period != LimitPeriod.Custom || (l.StartDate <= today && l.EndDate >= today))).OrderBy(l => l.Id).Take(take - users.Length).Select(l => new LimitSweepCandidate(true, l.Id, l.GroupId)).ToArrayAsync(token);
		return users.Concat(groups).ToArray();
	}
}
