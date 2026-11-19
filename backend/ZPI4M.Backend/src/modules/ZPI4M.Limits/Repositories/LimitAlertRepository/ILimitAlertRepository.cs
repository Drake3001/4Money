using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Repositories.LimitAlertRepository;

public interface ILimitAlertRepository
{
	Task LockOwnersAsync(IEnumerable<Guid> owners, CancellationToken token);
	Task<UserLimit[]> GetUsersAsync(Guid[] owners, CancellationToken token);
	Task<GroupLimit[]> GetGroupsAsync(Guid[] groups, CancellationToken token);
	Task<ILimitAlertState?> GetStateAsync(ILimitDefinition value, DateOnly period, CancellationToken token);
	ILimitAlertState AddState(ILimitDefinition value, DateOnly period);
}
