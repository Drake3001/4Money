using ZPI4M.Grouping.Services.Results;

namespace ZPI4M.Grouping.Services.Access;

public interface IGroupAccessService
{
	Task<GroupingResult<GroupAccessContext>> GetForMemberAsync(Guid actor, Guid groupId, CancellationToken cancellationToken = default);
	/// <summary>Requires an active Grouping transaction; the group lock lasts until its completion.</summary>
	Task<GroupingResult<GroupAccessContext>> LockForAdministratorAsync(Guid actor, Guid groupId, CancellationToken cancellationToken = default);
}
