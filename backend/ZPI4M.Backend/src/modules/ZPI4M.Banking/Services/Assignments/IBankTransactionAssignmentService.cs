using ZPI4M.Banking.Models;
using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.Services.Assignments;

/// <summary>
/// Internal registry operations for trusted business services, without user authorization or consumer validation.
/// Writes require an explicit transaction on the scoped BankingDbContext and never commit or roll it back.
/// Invalid IDs/types throw ArgumentException; missing transactions return NotFound, other consumers Conflict.
/// Database failures and cancellation propagate; the caller owns rollback and retries of its unit of work.
/// </summary>
public interface IBankTransactionAssignmentService
{
	/// <summary>Returns null when unassigned, including when the bank transaction does not exist. No explicit transaction is required.</summary>
	Task<BankTransactionAssignmentDetails?> GetAsync(Guid bankTransactionId, CancellationToken cancellationToken = default);

	/// <summary>Assigns a free transaction; an identical assignment is an idempotent success.</summary>
	Task<BankingResult<BankTransactionAssignmentDetails>> AssignAsync(Guid bankTransactionId,
		BankTransactionConsumerType consumerType, Guid consumerId, CancellationToken cancellationToken = default);

	/// <summary>Releases an exact match; returns false when already unassigned, without deleting another consumer's assignment.</summary>
	Task<BankingResult<bool>> ReleaseAsync(Guid bankTransactionId,
		BankTransactionConsumerType consumerType, Guid consumerId, CancellationToken cancellationToken = default);
}
