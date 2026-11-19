using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Transactions;

namespace ZPI4M.Grouping.Services.Access;

public sealed class GroupBudgetContextService(IGroupRepository groups, IGroupingTransactionCoordinator transactions) : IGroupBudgetContextService
{
	public async Task<GroupBudgetContext?> LockAsync(Guid groupId, CancellationToken cancellationToken = default)
	{
		transactions.EnsureActive();
		var group = await groups.LockAsync(groupId, cancellationToken);
		return group is null ? null : new(group.CurrencyCode, group.IsArchived);
	}
}
