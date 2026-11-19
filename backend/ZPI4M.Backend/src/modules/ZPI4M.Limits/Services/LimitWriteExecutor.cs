using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Services.Results;

namespace ZPI4M.Limits.Services;

public sealed class LimitWriteExecutor(LimitsDbContext db, IGroupingTransactionCoordinator transactions)
{
	public async Task<LimitsResult<T>> ExecuteAsync<T>(Func<CancellationToken, Task<LimitsResult<T>>> action, CancellationToken token)
	{
		try
		{
			return await transactions.ExecuteAsync(async ct =>
			{
				await transactions.EnlistAsync(db, ct);
				return await action(ct);
			}, r => r.Status is >= 200 and < 300, token);
		}
		catch (DbUpdateConcurrencyException)
		{
			return new(409, Code: "VersionConflict");
		}
		catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation, ConstraintName: "ex_user_limit_enabled_general" or "ex_user_limit_enabled_category" or "ex_group_limit_enabled_general" or "ex_group_limit_enabled_category" })
		{
			return new(409, Code: "OverlappingBudget");
		}
	}
}
