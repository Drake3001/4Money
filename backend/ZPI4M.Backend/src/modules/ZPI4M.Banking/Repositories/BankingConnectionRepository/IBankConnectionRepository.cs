using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Repositories.BankingConnectionRepository;

public interface IBankConnectionRepository
{
	Task<IReadOnlyList<BankConnection>> GetAllAsync(Guid userId, CancellationToken cancellationToken);
	Task<BankConnection?> GetAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken);
}
