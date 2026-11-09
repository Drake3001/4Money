using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Repositories.BankTransactionAssignmentRepository;

public sealed class BankTransactionAssignmentRepository(BankingDbContext database) : IBankTransactionAssignmentRepository
{
	public Task<BankTransactionAssignment?> GetAsync(Guid bankTransactionId, CancellationToken cancellationToken) =>
		database.BankTransactionAssignments.AsNoTracking()
			.SingleOrDefaultAsync(a => a.BankTransactionId == bankTransactionId, cancellationToken);

	public async Task<bool> LockTransactionAsync(Guid bankTransactionId, CancellationToken cancellationToken)
	{
		RequireTransaction();
		var id = await database.Database.SqlQuery<Guid>(
			$"SELECT _id AS \"Value\" FROM banking.bank_transaction WHERE _id = {bankTransactionId} FOR UPDATE")
			.SingleOrDefaultAsync(cancellationToken);
		return id != Guid.Empty;
	}

	public async Task TryAddAsync(Guid bankTransactionId, BankTransactionConsumerType consumerType, Guid consumerId, CancellationToken cancellationToken)
	{
		RequireTransaction();
		await database.Database.ExecuteSqlInterpolatedAsync($"""
			INSERT INTO banking.bank_transaction_assignment (bank_transaction_id, consumer_type, consumer_id)
			VALUES ({bankTransactionId}, {(int)consumerType}, {consumerId})
			ON CONFLICT (bank_transaction_id) DO NOTHING
			""", cancellationToken);
	}

	public async Task<bool> RemoveAsync(Guid bankTransactionId, BankTransactionConsumerType consumerType, Guid consumerId, CancellationToken cancellationToken)
	{
		RequireTransaction();
		return await database.BankTransactionAssignments
			.Where(a => a.BankTransactionId == bankTransactionId && a.ConsumerType == consumerType && a.ConsumerId == consumerId)
			.ExecuteDeleteAsync(cancellationToken) == 1;
	}

	private void RequireTransaction()
	{
		if (database.Database.CurrentTransaction is null)
			throw new InvalidOperationException("An explicit transaction on the scoped BankingDbContext is required for assignment writes.");
	}
}
