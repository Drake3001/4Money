namespace ZPI4M.Grouping.Services.Members;

/// <summary>Internal membership lookup; the caller authorizes and locks the group.</summary>
public interface IGroupMemberLookup
{
	Task<IReadOnlyList<Guid>> GetCurrentUserIdsAsync(Guid groupId, CancellationToken cancellationToken = default);
}
