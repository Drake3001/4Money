using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Grouping.Services.Transactions;

namespace ZPI4M.Grouping.Services.Access;

public sealed class GroupAccessService(IGroupRepository groups, IGroupMemberRepository members,
	IGroupingTransactionCoordinator transactions) : IGroupAccessService
{
	public async Task<GroupingResult<GroupAccessContext>> GetForMemberAsync(Guid actor, Guid groupId, CancellationToken cancellationToken = default)
	{
		var group = await groups.GetForActiveMemberAsync(groupId, actor, cancellationToken);
		return group is null ? new(404) : new(200, new(group.Id, group.CurrencyCode));
	}

	public Task<GroupingResult<GroupAccessContext>> LockForAdministratorAsync(Guid actor, Guid groupId, CancellationToken cancellationToken = default)
	{
		transactions.EnsureActive();
		return transactions.ExecuteAsync(async token =>
		{
			var group = await groups.LockAsync(groupId, token);
			if (group is null || group.IsArchived || !await members.IsMemberAsync(groupId, actor, token))
				return new GroupingResult<GroupAccessContext>(404);
			if (group.AdminUserId != actor) return new GroupingResult<GroupAccessContext>(403);
			return new GroupingResult<GroupAccessContext>(200, new(group.Id, group.CurrencyCode));
		}, result => result.Status == 200, cancellationToken);
	}
}
