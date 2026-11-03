using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.Repositories.GroupInvitationRepository;

public interface IGroupInvitationRepository
{
	Task<PagedResult<GroupInvitation>> GetPageAsync(Guid groupId, GroupInvitationQuery query, CancellationToken cancellationToken = default);
	Task<GroupInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
	Task<GroupInvitation?> LockAsync(Guid id, CancellationToken ct);
	void Add(GroupInvitation invitation);
}
