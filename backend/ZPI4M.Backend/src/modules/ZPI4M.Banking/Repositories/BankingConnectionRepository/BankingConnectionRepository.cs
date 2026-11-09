using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.Repositories.BankingConnectionRepository;

public sealed class BankingConnectionRepository(IDbContextFactory<BankingDbContext> contexts, TimeProvider time)
 : IBankConnectionAttemptRepository, IBankConnectionRepository
{
	public async Task AddAsync(BankConnectionAttempt attempt, CancellationToken cancellationToken)
	{
		await using var database = await contexts.CreateDbContextAsync(cancellationToken);
		database.ConnectionAttempts.Add(attempt);
		await database.SaveChangesAsync(cancellationToken);
	}

	public async Task<BankConnectionAttempt?> GetAsync(Guid userId, Guid attemptId, DateTimeOffset now, CancellationToken cancellationToken)
	{
		await using var database = await contexts.CreateDbContextAsync(cancellationToken);
		await ExpireAsync(database.ConnectionAttempts.Where(x => x.Id == attemptId && x.FourMoneyUserId == userId), now, cancellationToken);
		return await database.ConnectionAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attemptId && x.FourMoneyUserId == userId, cancellationToken);
	}

	public async Task<BankConnectionAttempt?> FindByStateAsync(string stateHash, DateTimeOffset now, CancellationToken cancellationToken)
	{
		await using var database = await contexts.CreateDbContextAsync(cancellationToken);
		var query = database.ConnectionAttempts.Where(x => x.CorrelationStateHash == stateHash);
		await ExpireAsync(query, now, cancellationToken);
		return await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
	}

	public async Task<bool> SetTinkUserAsync(Guid attemptId, string tinkUserId, DateTimeOffset now, CancellationToken cancellationToken)
	{
		await using var database = await contexts.CreateDbContextAsync(cancellationToken);
		return await database.ConnectionAttempts.Where(x => x.Id == attemptId && x.Status == BankConnectionAttemptStatus.Pending && x.ExpiresAt > now)
	  .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TinkUserId, tinkUserId).SetProperty(x => x.Version, x => x.Version + 1), cancellationToken) == 1;
	}

	public async Task<BankConnectionAttempt?> FinishAsync(Guid attemptId, BankConnectionAttemptStatus status,
	 string? failureCode, BankConnection? connection, CancellationToken cancellationToken)
	{
		await using var database = await contexts.CreateDbContextAsync(cancellationToken);
		await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
		var attempt = await database.ConnectionAttempts.FromSqlInterpolated(
		 $"SELECT * FROM banking.bank_connection_attempt WHERE _id = {attemptId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
		if (attempt is null || attempt.Status != BankConnectionAttemptStatus.Pending)
			return null;
		var now = time.GetUtcNow();
		if (attempt.ExpiresAt <= now)
		{
			attempt.Status = BankConnectionAttemptStatus.Expired;
			attempt.CompletedAt = now;
			attempt.Version++;
			await database.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			return null;
		}
		if (connection is not null)
		{
			if (connection.FourMoneyUserId != attempt.FourMoneyUserId || connection.TinkUserId != attempt.TinkUserId)
				return null;
			var existing = await database.Connections.SingleOrDefaultAsync(x => x.CredentialsId == connection.CredentialsId, cancellationToken);
			if (existing is not null && (existing.FourMoneyUserId != attempt.FourMoneyUserId || existing.TinkUserId != attempt.TinkUserId))
				return null;
			if (existing is null)
				database.Connections.Add(connection);
			attempt.BankConnectionId = existing?.Id ?? connection.Id;
		}
		attempt.Status = status;
		attempt.FailureCode = failureCode;
		attempt.CompletedAt = now;
		attempt.Version++;
		try
		{
			await database.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			return attempt;
		}
		catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
		{
			// Another attempt may concurrently have claimed the same credentials. No partial completion.
			return null;
		}
	}

	public async Task<IReadOnlyList<BankConnection>> GetAllAsync(Guid userId, CancellationToken cancellationToken)
	{
		await using var database = await contexts.CreateDbContextAsync(cancellationToken);
		return await database.Connections.AsNoTracking().Where(x => x.FourMoneyUserId == userId).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
	}

	public async Task<BankConnection?> GetAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken)
	{
		await using var database = await contexts.CreateDbContextAsync(cancellationToken);
		return await database.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == connectionId && x.FourMoneyUserId == userId, cancellationToken);
	}

	private static Task<int> ExpireAsync(IQueryable<BankConnectionAttempt> query, DateTimeOffset now, CancellationToken cancellationToken)
	 => query.Where(x => x.Status == BankConnectionAttemptStatus.Pending && x.ExpiresAt <= now)
	  .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, BankConnectionAttemptStatus.Expired)
	   .SetProperty(x => x.CompletedAt, now).SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
}
