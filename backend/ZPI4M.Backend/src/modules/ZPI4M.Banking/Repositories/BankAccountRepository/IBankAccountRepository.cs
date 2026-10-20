using ZPI4M.Banking.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Repositories.BankAccountRepository;

public interface IBankAccountRepository
{
	void Add(BankAccount entity);
	void Remove(BankAccount entity);
	Task<BankAccount?> GetByIdAsync(Guid bankAccountId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid bankAccountId, CancellationToken cancellationToken = default);
	Task<PagedResult<BankAccount>> GetPageAsync(BankAccountQuery query, CancellationToken cancellationToken = default);
	Task<BankAccount?> GetByAccountDetailsAsync(Guid userId, string financialInstitutionId, string number, CancellationToken cancellationToken = default);
}
