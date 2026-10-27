using ZPI4M.Banking.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Repositories.BankTransactionRepository;

public interface IBankTransactionRepository
{
	void Add(BankTransaction entity);
	void Remove(BankTransaction entity);
	Task<BankTransaction?> GetByIdAsync(Guid bankTransactionId, CancellationToken cancellationToken = default);
	Task<BankTransaction?> GetByIdForUserAsync(Guid userId, Guid bankTransactionId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid bankTransactionId, CancellationToken cancellationToken = default);
	Task<PagedResult<BankTransaction>> GetPageAsync(BankTransactionQuery query, CancellationToken cancellationToken = default);
	Task<BankTransaction?> GetByTinkTransactionIdAsync(string tinkTransactionId, CancellationToken cancellationToken = default);
	Task<bool> ExistsByTinkTransactionIdAsync(string tinkTransactionId, CancellationToken cancellationToken = default);
	void AddRange(IEnumerable<BankTransaction> transactions);
}
