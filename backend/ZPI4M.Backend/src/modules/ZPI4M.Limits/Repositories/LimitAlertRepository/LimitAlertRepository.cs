using Microsoft.EntityFrameworkCore;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Repositories.LimitAlertRepository;

public sealed class LimitAlertRepository(LimitsDbContext db) : ILimitAlertRepository
{
	// A namespace separates these locks from other advisory lock users. Take all owners in a stable order.
	public async Task LockOwnersAsync(IEnumerable<Guid> owners, CancellationToken token)
	{
		foreach (var owner in owners.Distinct().OrderBy(id => id.ToString("D"), StringComparer.Ordinal))
		{
			var key = "limits:user:" + owner.ToString("D");
			await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
		}
	}

	public Task<UserLimit[]> GetUsersAsync(Guid[] owners, CancellationToken token) => db.UserLimits.Where(l => l.IsEnabled && owners.Contains(l.UserId)).ToArrayAsync(token);
	public Task<GroupLimit[]> GetGroupsAsync(Guid[] groups, CancellationToken token) => db.GroupLimits.Where(l => l.IsEnabled && groups.Contains(l.GroupId)).ToArrayAsync(token);
	public async Task<ILimitAlertState?> GetStateAsync(ILimitDefinition value, DateOnly period, CancellationToken token) => value is GroupLimit ? await db.GroupLimitAlertStates.SingleOrDefaultAsync(s => s.LimitId == value.Id && s.PeriodStartDate == period, token) : await db.UserLimitAlertStates.SingleOrDefaultAsync(s => s.LimitId == value.Id && s.PeriodStartDate == period, token);
	public ILimitAlertState AddState(ILimitDefinition value, DateOnly period)
	{
		if (value is GroupLimit)
		{
			var state = new GroupLimitAlertState
			{
				LimitId = value.Id,
				PeriodStartDate = period
			};
			db.GroupLimitAlertStates.Add(state);
			return state;
		}

		var personal = new UserLimitAlertState
		{
			LimitId = value.Id,
			PeriodStartDate = period
		};
		db.UserLimitAlertStates.Add(personal);
		return personal;
	}
}
