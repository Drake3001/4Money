using ZPI4M.Banking.Models;
using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.Repositories;

public interface IBankConnectionAttemptRepository
{
	Task AddAsync(BankConnectionAttempt attempt, CancellationToken cancellationToken);
	Task<BankConnectionAttempt?> GetAsync(Guid userId, Guid attemptId, DateTimeOffset now, CancellationToken cancellationToken);
	Task<BankConnectionAttempt?> FindByStateAsync(string stateHash, DateTimeOffset now, CancellationToken cancellationToken);
	Task<bool> SetTinkUserAsync(Guid attemptId, string tinkUserId, DateTimeOffset now, CancellationToken cancellationToken);
	// Atomically checks expiry/state and saves the result with the optional connection.
	Task<BankConnectionAttempt?> FinishAsync(Guid attemptId, BankConnectionAttemptStatus status,
	 string? failureCode, BankConnection? connection, CancellationToken cancellationToken);
}
