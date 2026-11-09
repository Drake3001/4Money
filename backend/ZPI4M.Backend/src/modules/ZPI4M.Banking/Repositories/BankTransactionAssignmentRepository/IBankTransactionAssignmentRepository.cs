using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Repositories.BankTransactionAssignmentRepository;

public interface IBankTransactionAssignmentRepository
{
	Task<BankTransactionAssignment?> GetAsync(Guid bankTransactionId, CancellationToken cancellationToken);

	/// <summary>Requires the caller's explicit Banking transaction; holds the row lock until it ends.</summary>
	Task<bool> LockTransactionAsync(Guid bankTransactionId, CancellationToken cancellationToken);

	/// <summary>Inserts immediately in the caller's transaction; an existing assignment is left unchanged.</summary>
	Task TryAddAsync(Guid bankTransactionId, BankTransactionConsumerType consumerType, Guid consumerId, CancellationToken cancellationToken);

	/// <summary>Deletes only an exact match, immediately in the caller's transaction.</summary>
	Task<bool> RemoveAsync(Guid bankTransactionId, BankTransactionConsumerType consumerType, Guid consumerId, CancellationToken cancellationToken);
}
