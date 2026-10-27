using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Services.Reads;

/// <summary>
/// Reads locally persisted banking history without contacting Tink or starting synchronization.
/// Callers must supply a trusted local user ID (HTTP callers derive it from validated authentication).
/// Missing users/resources and foreign resources return NotFound; inactive users return Forbidden.
/// Invalid arguments throw ArgumentException; database failures and cancellation propagate.
/// </summary>
public interface IBankingReadService
{
	Task<BankingResult<PagedResult<BankAccountDetails>>> GetAccountsAsync(
		Guid userId, BankAccountReadQuery query, CancellationToken cancellationToken = default);
	Task<BankingResult<BankAccountDetails>> GetAccountAsync(
		Guid userId, Guid accountId, CancellationToken cancellationToken = default);
	/// <summary>An explicit missing or foreign BankAccountId filter also returns NotFound.</summary>
	Task<BankingResult<PagedResult<BankTransactionDetails>>> GetTransactionsAsync(
		Guid userId, BankTransactionReadQuery query, CancellationToken cancellationToken = default);
	Task<BankingResult<BankTransactionDetails>> GetTransactionAsync(
		Guid userId, Guid transactionId, CancellationToken cancellationToken = default);
}
