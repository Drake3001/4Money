using ZPI4M.Grouping.Repositories.GroupRepository;

namespace ZPI4M.Grouping.Services.ExpenseMetadata;

/// <summary>Internal read after Expense authorization, including archived groups.</summary>
public sealed class GroupExpenseMetadataLookup(IGroupRepository groups) : IGroupExpenseMetadataLookup
{
	public Task<IReadOnlyList<Guid>> GetActiveGroupIdsAsync(Guid actor, CancellationToken cancellationToken = default) =>
		groups.GetActiveExpenseGroupIdsAsync(actor, cancellationToken);

	public async Task<GroupExpenseMetadataResult> GetAsync(Guid groupId, CancellationToken cancellationToken = default)
	{
		var group = await groups.GetReadAsync(groupId, cancellationToken);
		return group is null ? new(404) : new(200, new(group.Id, group.Name, group.CurrencyCode, group.IsArchived));
	}
}
