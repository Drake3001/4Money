using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;
namespace ZPI4M.Grouping.Repositories.GroupSettlementRepository;

public sealed class GroupSettlementQuery : PagedQuery
{
	public Guid? FromUserId { get; set; }
	public Guid? ToUserId { get; set; }
	public DateTimeOffset? DateFrom { get; set; }
	public DateTimeOffset? DateTo { get; set; }
	public SettlementMethod? Method { get; set; }
}
