using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Aggregates;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Grouping.Services.Transactions;

namespace ZPI4M.Grouping.Services.Expenses;

/// <summary>Validates and applies expense contribution changes inside the caller's unit of work.</summary>
public sealed class GroupExpenseContributionService(GroupingDbContext db, IGroupRepository groups,
	IGroupMemberRepository members, IGroupMemberAggregateService aggregates,
	IGroupingTransactionCoordinator transactions) : IGroupExpenseContributionService
{
	public Task<GroupingResult<GroupExpenseMetadata>> ApplyAsync(Guid actor, ExpenseContribution contribution,
		string expectedCurrency, CancellationToken cancellationToken = default) =>
		ApplyCoreAsync(actor, null, contribution, expectedCurrency, cancellationToken);

	public Task<GroupingResult<GroupExpenseMetadata>> ApplyChangeAsync(Guid actor, ExpenseContribution previous,
		ExpenseContribution? current, string expectedCurrency, CancellationToken cancellationToken = default) =>
		ApplyCoreAsync(actor, previous, current, expectedCurrency, cancellationToken);

	private Task<GroupingResult<GroupExpenseMetadata>> ApplyCoreAsync(Guid actor, ExpenseContribution? previous,
		ExpenseContribution? current, string expectedCurrency, CancellationToken cancellationToken)
	{
		transactions.EnsureActive();
		return transactions.ExecuteAsync(async token =>
		{
			var group = await groups.LockAsync((current ?? previous)!.GroupId, token);
			if (group is null) return new GroupingResult<GroupExpenseMetadata>(404);
			var users = (current?.Shares.Select(share => share.UserId).Append(current.PaidByUserId) ?? [])
				.Append(actor).Distinct().ToArray();
			var memberships = await members.GetManyAsync(group.Id, users, token);
			bool IsActive(Guid user) => memberships.TryGetValue(user, out var member) && member.IsMember;
			var canRead = IsActive(actor) || previous?.PaidByUserId == actor ||
				previous?.Shares.Any(share => share.UserId == actor) == true;
			if (!IsActive(actor) || group.IsArchived)
				return new GroupingResult<GroupExpenseMetadata>(previous is not null && canRead ? 403 : 404);
			if (group.CurrencyCode != expectedCurrency) return Invalid("GroupId", "Group currency must match the expense currency.");
			if (current is not null)
			{
				if (current.PaidByUserId != previous?.PaidByUserId && !IsActive(current.PaidByUserId))
					return Invalid("PayerId", "Payer must be a current member of the group.");
				var previousUsers = previous?.Shares.Select(share => share.UserId).ToHashSet();
				var invalid = current.Shares.Select((share, index) => (share, index))
					.Where(pair => !IsActive(pair.share.UserId) && previousUsers?.Contains(pair.share.UserId) != true).ToArray();
				if (invalid.Length > 0)
					return new GroupingResult<GroupExpenseMetadata>(400, Errors: invalid.ToDictionary(
						pair => $"Shares[{pair.index}].UserId", _ => new[] { "Participant must be a current member of the group." }));
			}
			try { await aggregates.ApplyExpenseChangeAsync(previous, current, token); }
			catch (AggregateInvariantException) { return new GroupingResult<GroupExpenseMetadata>(409); }
			await db.SaveChangesAsync(token);
			return new GroupingResult<GroupExpenseMetadata>(200, new(group.Id, group.Name, group.CurrencyCode, group.IsArchived));
		}, result => result.Status == 200, cancellationToken);
	}

	private static GroupingResult<GroupExpenseMetadata> Invalid(string field, string message) => new(400,
		Errors: new Dictionary<string, string[]> { [field] = [message] });
}
