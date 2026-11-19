using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Repositories.LimitSweepRepository;

public interface ILimitSweepRepository
{
	Task<ILimitDefinition?> GetAsync(LimitSweepCandidate candidate, CancellationToken token);
	Task<LimitSweepCandidate[]> PageAsync(bool group, Guid after, int take, DateOnly today, CancellationToken token);
}
