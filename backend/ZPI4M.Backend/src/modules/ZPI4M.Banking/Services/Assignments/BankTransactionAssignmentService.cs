using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories.BankTransactionAssignmentRepository;
using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.Services.Assignments;

public sealed class BankTransactionAssignmentService(IBankTransactionAssignmentRepository assignments) : IBankTransactionAssignmentService
{
	public async Task<BankTransactionAssignmentDetails?> GetAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
	{
		ValidateId(bankTransactionId, nameof(bankTransactionId));
		cancellationToken.ThrowIfCancellationRequested();
		var assignment = await assignments.GetAsync(bankTransactionId, cancellationToken);
		return assignment is null ? null : Map(assignment);
	}

	public async Task<BankingResult<BankTransactionAssignmentDetails>> AssignAsync(Guid bankTransactionId,
		BankTransactionConsumerType consumerType, Guid consumerId, CancellationToken cancellationToken = default)
	{
		Validate(bankTransactionId, consumerType, consumerId);
		cancellationToken.ThrowIfCancellationRequested();
		if (!await assignments.LockTransactionAsync(bankTransactionId, cancellationToken))
			return BankingResult<BankTransactionAssignmentDetails>.Failure(BankingErrorCode.NotFound);

		await assignments.TryAddAsync(bankTransactionId, consumerType, consumerId, cancellationToken);
		var assignment = await assignments.GetAsync(bankTransactionId, cancellationToken);
		return assignment is not null && Matches(assignment, consumerType, consumerId)
			? BankingResult<BankTransactionAssignmentDetails>.Success(Map(assignment))
			: BankingResult<BankTransactionAssignmentDetails>.Failure(BankingErrorCode.Conflict);
	}

	public async Task<BankingResult<bool>> ReleaseAsync(Guid bankTransactionId,
		BankTransactionConsumerType consumerType, Guid consumerId, CancellationToken cancellationToken = default)
	{
		Validate(bankTransactionId, consumerType, consumerId);
		cancellationToken.ThrowIfCancellationRequested();
		if (!await assignments.LockTransactionAsync(bankTransactionId, cancellationToken))
			return BankingResult<bool>.Failure(BankingErrorCode.NotFound);

		var assignment = await assignments.GetAsync(bankTransactionId, cancellationToken);
		if (assignment is null) return BankingResult<bool>.Success(false);
		if (!Matches(assignment, consumerType, consumerId)) return BankingResult<bool>.Failure(BankingErrorCode.Conflict);
		return BankingResult<bool>.Success(await assignments.RemoveAsync(bankTransactionId, consumerType, consumerId, cancellationToken));
	}

	private static bool Matches(BankTransactionAssignment assignment, BankTransactionConsumerType consumerType, Guid consumerId) =>
		assignment.ConsumerType == consumerType && assignment.ConsumerId == consumerId;

	private static BankTransactionAssignmentDetails Map(BankTransactionAssignment assignment) =>
		new(assignment.BankTransactionId, assignment.ConsumerType, assignment.ConsumerId);

	private static void Validate(Guid bankTransactionId, BankTransactionConsumerType consumerType, Guid consumerId)
	{
		ValidateId(bankTransactionId, nameof(bankTransactionId));
		ValidateId(consumerId, nameof(consumerId));
		if (!Enum.IsDefined(consumerType)) throw new ArgumentOutOfRangeException(nameof(consumerType));
	}

	private static void ValidateId(Guid id, string name)
	{
		if (id == Guid.Empty) throw new ArgumentException("A nonempty local ID is required.", name);
	}
}
