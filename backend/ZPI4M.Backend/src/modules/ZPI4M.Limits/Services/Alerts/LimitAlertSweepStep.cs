using ZPI4M.Grouping.Services.Access;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Repositories.LimitSweepRepository;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Limits.Services.Alerts;

public enum AlertSweepStepOutcome
{
	Skipped,
	Evaluated,
	Notified
}

public sealed class LimitAlertSweepStep(LimitsDbContext db, ILimitSweepRepository repository, IGroupingTransactionCoordinator transactions, IGroupBudgetContextService groups, IUserLookupService users, LimitAlertService alerts)
{
	public Task<AlertSweepStepOutcome> ExecuteAsync(LimitSweepCandidate candidate, DateOnly today, CancellationToken token) => transactions.ExecuteAsync(async ct =>
	{
		await transactions.EnlistAsync(db, ct);
		if (candidate.IsGroup)
		{
			var group = await groups.LockAsync(candidate.OwnerId, ct);
			if (group is null || group.IsArchived || group.CurrencyCode != "PLN")
				return AlertSweepStepOutcome.Skipped;
			if (await repository.GetAsync(candidate, ct) is not GroupLimit { IsEnabled: true } limit)
				return AlertSweepStepOutcome.Skipped;
			return await alerts.EvaluateSystemAsync(limit, today, ct) ? AlertSweepStepOutcome.Notified : AlertSweepStepOutcome.Evaluated;
		}
		else
		{
			await alerts.LockOwnersAsync([candidate.OwnerId], ct);
			if (!(await users.GetActiveUserIdsAsync([candidate.OwnerId], ct)).Contains(candidate.OwnerId))
				return AlertSweepStepOutcome.Skipped;
			if (await repository.GetAsync(candidate, ct) is not UserLimit { IsEnabled: true } limit)
				return AlertSweepStepOutcome.Skipped;
			return await alerts.EvaluateAsync(candidate.OwnerId, limit, today, ct) ? AlertSweepStepOutcome.Notified : AlertSweepStepOutcome.Evaluated;
		}
	}, _ => true, token);
}
