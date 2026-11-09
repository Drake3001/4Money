using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories.BankTransactionAssignmentRepository;
using ZPI4M.Banking.Services.Assignments;
using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class BankTransactionAssignmentServiceTests : PostgresFixture
{
	private static readonly Guid TransactionId = new("50000000-0000-0000-0000-000000000003");
	private static readonly Guid ConsumerId = new("70000000-0000-0000-0000-000000000010");
	private static readonly Guid OtherConsumerId = new("70000000-0000-0000-0000-000000000011");
	private BankingDbContext Database() => new(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(ConnectionString).Options);
	private static BankTransactionAssignmentService Service(BankingDbContext db) => new(new BankTransactionAssignmentRepository(db));
	private Task SeedAsync() => SeedScript.ExecuteAsync(ConnectionString,
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));

	[PostgresTheory]
	[InlineData(BankTransactionConsumerType.Expense)]
	[InlineData(BankTransactionConsumerType.GroupSettlement)]
	public async Task AssignmentAndReleaseAreIdempotentAndConflictsLeaveTransactionUsable(BankTransactionConsumerType type)
	{
		await SeedAsync();
		await using var db = Database();
		var service = Service(db);
		Assert.Null(await service.GetAsync(TransactionId));
		Assert.Null(await service.GetAsync(Guid.NewGuid()));
		await using var transaction = await db.Database.BeginTransactionAsync();
		var expected = new BankTransactionAssignmentDetails(TransactionId, type, ConsumerId);
		Assert.Equal(expected, (await service.AssignAsync(TransactionId, type, ConsumerId)).Value);
		Assert.Equal(expected, (await service.AssignAsync(TransactionId, type, ConsumerId)).Value);
		Assert.Equal(expected, await service.GetAsync(TransactionId));
		var otherType = type == BankTransactionConsumerType.Expense ? BankTransactionConsumerType.GroupSettlement : BankTransactionConsumerType.Expense;
		foreach (var (conflictingType, conflictingId) in new[] { (type, OtherConsumerId), (otherType, ConsumerId) })
		{
			Assert.Equal(BankingErrorCode.Conflict, (await service.AssignAsync(TransactionId, conflictingType, conflictingId)).Error);
			Assert.Equal(BankingErrorCode.Conflict, (await service.ReleaseAsync(TransactionId, conflictingType, conflictingId)).Error);
			Assert.Equal(expected, await service.GetAsync(TransactionId));
		}
		Assert.True((await service.ReleaseAsync(TransactionId, type, ConsumerId)).Value);
		Assert.False((await service.ReleaseAsync(TransactionId, type, ConsumerId)).Value);
		Assert.Null(await service.GetAsync(TransactionId));
		Assert.Equal(BankingErrorCode.NotFound, (await service.AssignAsync(Guid.NewGuid(), type, ConsumerId)).Error);
		Assert.Equal(BankingErrorCode.NotFound, (await service.ReleaseAsync(Guid.NewGuid(), type, ConsumerId)).Error);
		// Expected conflicts and missing rows have not aborted or committed the caller's transaction.
		Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync());
		Assert.Same(transaction, db.Database.CurrentTransaction);
		Assert.Empty(db.ChangeTracker.Entries());
		await transaction.CommitAsync();
	}

	[PostgresFact]
	public async Task WritesWithoutExplicitTransactionAreRejectedEvenForMissingResources()
	{
		await SeedAsync();
		await using var db = Database();
		var service = Service(db);
		foreach (var id in new[] { TransactionId, Guid.NewGuid() })
		{
			await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignAsync(id, BankTransactionConsumerType.Expense, ConsumerId));
			await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReleaseAsync(id, BankTransactionConsumerType.Expense, ConsumerId));
			Assert.Null(await service.GetAsync(id));
		}
		// Repository write methods also enforce the transaction contract when called directly.
		var repository = new BankTransactionAssignmentRepository(db);
		await Assert.ThrowsAsync<InvalidOperationException>(() => repository.TryAddAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId, default));
		await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RemoveAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId, default));
	}

	[PostgresFact]
	public async Task CommitAndRollbackControlAssignmentAndReleaseWithoutFlushingOrClearingTrackedChanges()
	{
		await SeedAsync();
		await using var db = Database();
		var service = Service(db);
		var bankTransaction = await db.BankTransactions.SingleAsync(t => t.Id == TransactionId);
		var originalDescription = bankTransaction.Description;
		bankTransaction.Description = "pending synthetic edit";
		db.ChangeTracker.DetectChanges();
		await using (var transaction = await db.Database.BeginTransactionAsync())
		{
			Assert.True((await service.AssignAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).IsSuccess);
			Assert.Same(transaction, db.Database.CurrentTransaction);
			await using var observer = Database();
			Assert.Null(await Service(observer).GetAsync(TransactionId));
			Assert.Equal(originalDescription, await observer.BankTransactions.Where(t => t.Id == TransactionId).Select(t => t.Description).SingleAsync());
			Assert.Equal(EntityState.Modified, db.Entry(bankTransaction).State);
			await transaction.RollbackAsync();
		}
		Assert.Null(await service.GetAsync(TransactionId));
		Assert.Equal(EntityState.Modified, db.Entry(bankTransaction).State);
		await using (var transaction = await db.Database.BeginTransactionAsync())
		{
			Assert.True((await service.AssignAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).IsSuccess);
			await transaction.CommitAsync();
		}
		await using var verification = Database();
		Assert.NotNull(await Service(verification).GetAsync(TransactionId));
		Assert.Equal(originalDescription, await verification.BankTransactions.Where(t => t.Id == TransactionId).Select(t => t.Description).SingleAsync());
		await using (var transaction = await db.Database.BeginTransactionAsync())
		{
			Assert.True((await service.ReleaseAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).Value);
			Assert.Null(await service.GetAsync(TransactionId));
			await transaction.RollbackAsync();
		}
		Assert.NotNull(await service.GetAsync(TransactionId));
		await using (var transaction = await db.Database.BeginTransactionAsync())
		{
			Assert.True((await service.ReleaseAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).Value);
			await transaction.CommitAsync();
		}
		Assert.Null(await Service(verification).GetAsync(TransactionId));
		Assert.Equal(EntityState.Modified, db.Entry(bankTransaction).State);
	}

	[PostgresFact]
	public async Task RepositoryDeleteOnlyRemovesAnExactMatch()
	{
		await SeedAsync();
		await using var db = Database();
		await using var transaction = await db.Database.BeginTransactionAsync();
		var service = Service(db);
		Assert.True((await service.AssignAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).IsSuccess);
		var repository = new BankTransactionAssignmentRepository(db);
		Assert.False(await repository.RemoveAsync(TransactionId, BankTransactionConsumerType.GroupSettlement, ConsumerId, default));
		Assert.False(await repository.RemoveAsync(TransactionId, BankTransactionConsumerType.Expense, OtherConsumerId, default));
		Assert.False(await repository.RemoveAsync(Guid.NewGuid(), BankTransactionConsumerType.Expense, ConsumerId, default));
		Assert.NotNull(await service.GetAsync(TransactionId));
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ConcurrentAssignmentsWaitForCallerCommitAndRespectConsumerIdentity(bool sameConsumer)
	{
		await SeedAsync();
		await using var first = Database();
		await using var second = Database();
		await using var firstTransaction = await first.Database.BeginTransactionAsync();
		await using var secondTransaction = await second.Database.BeginTransactionAsync();
		var secondPid = await second.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
		Assert.True((await Service(first).AssignAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).IsSuccess);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		var otherId = sameConsumer ? ConsumerId : OtherConsumerId;
		var pending = Service(second).AssignAsync(TransactionId, BankTransactionConsumerType.Expense, otherId, timeout.Token);
		await WaitForLockAsync(secondPid, timeout.Token);
		Assert.False(pending.IsCompleted);
		await firstTransaction.CommitAsync();
		var result = await pending;
		if (sameConsumer) Assert.True(result.IsSuccess);
		else Assert.Equal(BankingErrorCode.Conflict, result.Error);
		Assert.Equal(1, await second.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync());
		await secondTransaction.CommitAsync();
		await using var verification = Database();
		Assert.Equal(ConsumerId, (await Service(verification).GetAsync(TransactionId))!.ConsumerId);
		Assert.Equal(1, await verification.BankTransactionAssignments.CountAsync(a => a.BankTransactionId == TransactionId));
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ConcurrentAssignmentAndReleaseAreSerializedInBothOrders(bool releaseFirst)
	{
		await SeedAsync();
		await using var first = Database();
		await using var second = Database();
		if (releaseFirst)
		{
			await using var setup = await first.Database.BeginTransactionAsync();
			Assert.True((await Service(first).AssignAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).IsSuccess);
			await setup.CommitAsync();
		}
		await using var firstTransaction = await first.Database.BeginTransactionAsync();
		await using var secondTransaction = await second.Database.BeginTransactionAsync();
		var secondPid = await second.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
		if (releaseFirst) Assert.True((await Service(first).ReleaseAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).Value);
		else Assert.True((await Service(first).AssignAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).IsSuccess);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		Task<BankingResult<BankTransactionAssignmentDetails>>? assign = null;
		Task<BankingResult<bool>>? release = null;
		if (releaseFirst) assign = Service(second).AssignAsync(TransactionId, BankTransactionConsumerType.GroupSettlement, OtherConsumerId, timeout.Token);
		else release = Service(second).ReleaseAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId, timeout.Token);
		await WaitForLockAsync(secondPid, timeout.Token);
		await firstTransaction.CommitAsync();
		if (releaseFirst) Assert.True((await assign!).IsSuccess);
		else Assert.True((await release!).Value);
		await secondTransaction.CommitAsync();
		await using var verification = Database();
		var remaining = await Service(verification).GetAsync(TransactionId);
		if (releaseFirst) Assert.Equal(new BankTransactionAssignmentDetails(TransactionId, BankTransactionConsumerType.GroupSettlement, OtherConsumerId), remaining);
		else Assert.Null(remaining);
	}

	private async Task WaitForLockAsync(int pid, CancellationToken ct)
	{
		// Each poll runs outside the writers' transactions, so pg_stat_activity gets a fresh snapshot.
		await using var observer = Database();
		while (!await observer.Database.SqlQuery<bool>($"""
			SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock') AS "Value"
			""").SingleAsync(ct))
			await Task.Delay(25, ct);
	}
}
