using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;
namespace ZPI4M.Grouping.Repositories.GroupSettlementRepository;

public interface IGroupSettlementRepository
{
	void Add(GroupSettlement value); void Remove(GroupSettlement value);
	Task<GroupSettlement?> GetAsync(Guid groupId, Guid id, bool tracking, CancellationToken ct);
	Task<PagedResult<GroupSettlement>> GetPageAsync(Guid groupId, GroupSettlementQuery query, CancellationToken ct);
}
