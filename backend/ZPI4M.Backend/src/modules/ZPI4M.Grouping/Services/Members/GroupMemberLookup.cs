using ZPI4M.Grouping.Repositories.GroupMemberRepository;

namespace ZPI4M.Grouping.Services.Members;

public sealed class GroupMemberLookup(IGroupMemberRepository members) : IGroupMemberLookup
{
	public Task<IReadOnlyList<Guid>> GetCurrentUserIdsAsync(Guid groupId, CancellationToken cancellationToken = default) =>
		members.GetActiveUserIdsAsync(groupId, ct: cancellationToken);
}
