using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories.BankingConnectionRepository;
using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class ConnectionPersistenceIsolationTests : PostgresFixture
{
	[PostgresFact]
	public async Task ConnectionOperationsNeitherFlushNorDetachUnrelatedScopedChanges()
	{
		var user = await AddUserAsync(tinkUserId: "synthetic-user");
		using var session = OpenSession();
		var modified = Account(user, "original");
		var deleted = Account(user, "to delete");
		session.Database.BankAccounts.AddRange(modified, deleted);
		await session.Database.SaveChangesAsync();
		modified.Name = "pending edit";
		session.Database.BankAccounts.Remove(deleted);
		var added = Account(user, "pending addition");
		session.Database.BankAccounts.Add(added);
		session.Database.ChangeTracker.DetectChanges();
		await using var callerTransaction = await session.Database.Database.BeginTransactionAsync();

		var start = await session.Service.StartConnectionAsync(user);
		Assert.True(start.IsSuccess);
		var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(start.Value!.AuthorizationUrl).Query)["state"].ToString();
		var completed = await session.Service.CompleteAsync(new BankingCallback { State = state, CredentialsId = "synthetic-credential" });
		Assert.True(completed.IsSuccess);
		Assert.True((await session.Service.GetConnectionAttemptAsync(user, start.Value.AttemptId)).IsSuccess);
		Assert.Single((await session.Service.GetConnectionsAsync(user)).Value!);
		Assert.True((await session.Service.GetConnectionAsync(user, completed.Value!.ConnectionId!.Value)).IsSuccess);
		Assert.Same(callerTransaction, session.Database.Database.CurrentTransaction);
		Assert.Equal(EntityState.Modified, session.Database.Entry(modified).State);
		Assert.Equal(EntityState.Deleted, session.Database.Entry(deleted).State);
		Assert.Equal(EntityState.Added, session.Database.Entry(added).State);
		Assert.Equal("pending edit", modified.Name);
		await using (var observer = Database())
		{
			Assert.Equal("original", (await observer.BankAccounts.SingleAsync(a => a.Id == modified.Id)).Name);
			Assert.True(await observer.BankAccounts.AnyAsync(a => a.Id == deleted.Id));
			Assert.False(await observer.BankAccounts.AnyAsync(a => a.Id == added.Id));
			Assert.Equal(BankConnectionAttemptStatus.Completed, (await observer.ConnectionAttempts.SingleAsync()).Status);
			Assert.Single(await observer.Connections.ToListAsync());
		}

		await session.Database.SaveChangesAsync();
		await callerTransaction.CommitAsync();
		await using var check = Database();
		Assert.Equal("pending edit", (await check.BankAccounts.SingleAsync(a => a.Id == modified.Id)).Name);
		Assert.False(await check.BankAccounts.AnyAsync(a => a.Id == deleted.Id));
		Assert.True(await check.BankAccounts.AnyAsync(a => a.Id == added.Id));
	}

	[PostgresFact]
	public async Task CompletionFailureRollsBackBothRecordsAndNextOperationUsesFreshContext()
	{
		var user = await AddUserAsync(tinkUserId: "synthetic-user");
		using var session = OpenSession();
		var start = (await session.Service.StartConnectionAsync(user)).Value!;
		var interceptor = new FailCompletedSave();
		var options = new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(ConnectionString)
			.AddInterceptors(interceptor).Options;
		var repository = new BankingConnectionRepository(new BankingContextFactory(options), Clock);
		var connectionId = Guid.NewGuid();
		BankConnection Connection() => new()
		{
			Id = connectionId,
			FourMoneyUserId = user,
			TinkUserId = "synthetic-user",
			CredentialsId = "synthetic-credential",
			ConnectionStatus = BankConnectionStatus.Connected,
			SyncStatus = BankSynchronizationStatus.NotStarted,
			CreatedAt = Clock.GetUtcNow(),
			ConnectedAt = Clock.GetUtcNow()
		};
		await Assert.ThrowsAsync<DbUpdateException>(() => repository.FinishAsync(start.AttemptId,
			BankConnectionAttemptStatus.Completed, null, Connection(), default));
		await using (var observer = Database())
		{
			Assert.Empty(await observer.Connections.ToListAsync());
			var attempt = await observer.ConnectionAttempts.SingleAsync();
			Assert.Equal(BankConnectionAttemptStatus.Pending, attempt.Status);
			Assert.Null(attempt.BankConnectionId);
			Assert.Null(attempt.CompletedAt);
		}
		Assert.NotNull(await repository.FinishAsync(start.AttemptId, BankConnectionAttemptStatus.Completed, null, Connection(), default));
		await using var check = Database();
		Assert.Equal(connectionId, (await check.Connections.SingleAsync()).Id);
		Assert.Equal(BankConnectionAttemptStatus.Completed, (await check.ConnectionAttempts.SingleAsync()).Status);
	}

	private BankingDbContext Database() => new(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(ConnectionString).Options);
	private static BankAccount Account(Guid user, string name) => new()
	{
		Id = Guid.NewGuid(),
		FkUserId = user,
		Name = name,
		CurrencyCode = "PLN"
	};

	private sealed class FailCompletedSave : SaveChangesInterceptor
	{
		private bool _failed;
		public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
			CancellationToken cancellationToken = default)
		{
			if (!_failed && eventData.Context!.ChangeTracker.Entries<BankConnectionAttempt>()
				.Any(e => e.Entity.Status == BankConnectionAttemptStatus.Completed))
			{
				_failed = true;
				throw new DbUpdateException("synthetic failure after writes, before commit");
			}
			return ValueTask.FromResult(result);
		}
	}
}
