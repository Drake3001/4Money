using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Services.Refresh;
using ZPI4M.Banking.Tink;

namespace ZPI4M.Banking.Repositories.BankRefreshRepository;

public sealed class BankRefreshRepository(BankingDbContext database, TimeProvider clock, IOptions<BankRefreshOptions> options)
{
	public async Task<BankRefresh> EnqueueAsync(Guid userId, string tinkUserId, CancellationToken ct)
	{
		await using var transaction = await database.Database.BeginTransactionAsync(ct);
		// Serialize enqueue on the existing user row; the filtered index is a second invariant.
		var user = await database.Set<ZPI4M.User.Models.User>()
			.FromSqlInterpolated($"SELECT * FROM \"user\".\"user\" WHERE _id = {userId} FOR UPDATE").SingleAsync(ct);
		if (!user.IsActive || user.TinkUserId != tinkUserId) throw new RefreshIdentityException();
		var active = await database.Refreshes.AsNoTracking().SingleOrDefaultAsync(x => x.FourMoneyUserId == userId
			&& (x.Status == "QUEUED" || x.Status == "RUNNING"), ct);
		if (active is not null) return active;
		var now = clock.GetUtcNow();
		var job = new BankRefresh { Id = Guid.NewGuid(), FourMoneyUserId = userId, TinkUserId = tinkUserId, CreatedAt = now, NextAttemptAt = now };
		database.Refreshes.Add(job);
		await database.SaveChangesAsync(ct);
		await transaction.CommitAsync(ct);
		return job;
	}

	public Task<BankRefresh?> GetAsync(Guid userId, Guid id, CancellationToken ct) => database.Refreshes.AsNoTracking()
		.Include(x => x.Accounts).SingleOrDefaultAsync(x => x.Id == id && x.FourMoneyUserId == userId, ct);

	public async Task<BankRefresh?> ClaimAsync(CancellationToken ct)
	{
		database.ChangeTracker.Clear();
		var now = clock.GetUtcNow();
		await using var transaction = await database.Database.BeginTransactionAsync(ct);
		var jobs = await database.Refreshes.FromSqlInterpolated($"""
			SELECT * FROM banking.bank_refresh
			WHERE (status = 'QUEUED' AND next_attempt_at <= {now})
			 OR (status = 'RUNNING' AND lease_expires_at <= {now})
			ORDER BY created_at LIMIT 1 FOR UPDATE SKIP LOCKED
			""").ToListAsync(ct);
		var job = jobs.SingleOrDefault();
		if (job is null) return null;
		job.Status = "RUNNING";
		job.StartedAt ??= now;
		job.LeaseId = Guid.NewGuid();
		job.LeaseExpiresAt = now.AddSeconds(options.Value.LeaseSeconds);
		job.Attempts++;
		await database.SaveChangesAsync(ct);
		await transaction.CommitAsync(ct);
		return job;
	}

	public async Task<bool> RenewAsync(Guid id, Guid leaseId, CancellationToken ct)
	{
		var now = clock.GetUtcNow();
		return await database.Refreshes.Where(x => x.Id == id && x.LeaseId == leaseId
			&& x.Status == "RUNNING" && x.LeaseExpiresAt > now)
			.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeaseExpiresAt, now.AddSeconds(options.Value.LeaseSeconds)), ct) == 1;
	}

	public async Task DiscoverAsync(BankRefresh claimed, IReadOnlyList<TinkBankAccount> accounts, CancellationToken ct)
	{
		await WithLeaseAsync(claimed, async job =>
		{
			if (job.DiscoveryCompleted) return;
			var local = await database.BankAccounts.Where(x => x.TinkUserId == job.TinkUserId).ToListAsync(ct);
			var remote = accounts.ToDictionary(x => x.Id, StringComparer.Ordinal);
			foreach (var source in accounts)
			{
				var account = local.SingleOrDefault(x => x.TinkAccountId == source.Id);
				if (account is not null && account.FkUserId != job.FourMoneyUserId) throw new RefreshIdentityException();
				if (account is null)
				{
					account = new BankAccount { Id = Guid.NewGuid(), FkUserId = job.FourMoneyUserId, TinkUserId = job.TinkUserId, TinkAccountId = source.Id };
					database.BankAccounts.Add(account);
				}
				account.Name = source.Name;
				account.Number = source.Number;
				account.Iban = source.Iban;
				account.FinancialInstitutionId = source.FinancialInstitutionId;
				account.Type = source.Type;
				account.CurrencyCode = source.CurrencyCode;
				account.IsClosed = false;
				var through = job.StartedAt!.Value;
				job.Accounts.Add(new BankRefreshAccount
				{
					RefreshId = job.Id,
					AccountId = account.Id,
					TinkAccountId = source.Id,
					Through = through,
					LastSuccessfulSyncAt = account.LastSuccessfulSyncAt,
					From = account.TransactionsSyncedThrough?.AddDays(-options.Value.OverlapDays)
						?? through.AddDays(-options.Value.InitialHistoryDays)
				});
			}
			foreach (var missing in local.Where(x => x.FkUserId == job.FourMoneyUserId && x.TinkAccountId is not null && !remote.ContainsKey(x.TinkAccountId)))
				missing.IsClosed = true;
			job.DiscoveryCompleted = true;
		}, ct);
	}

	public async Task SaveAccountAsync(BankRefresh claimed, Guid accountId, IReadOnlyList<TinkBankTransaction> transactions, CancellationToken ct)
	{
		await WithLeaseAsync(claimed, async job =>
		{
			var result = await database.RefreshAccounts.SingleAsync(x => x.RefreshId == job.Id && x.AccountId == accountId, ct);
			if (result.Status == "SUCCEEDED") return;
			var account = await database.BankAccounts.SingleAsync(x => x.Id == accountId && x.FkUserId == job.FourMoneyUserId
				&& x.TinkUserId == job.TinkUserId && x.TinkAccountId == result.TinkAccountId, ct);
			var ids = transactions.Select(x => x.Id).ToArray();
			if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || transactions.Any(x => x.AccountId != result.TinkAccountId))
				throw new Tink.Http.TinkApiException("transactions", "invalid_response");
			var existing = await database.BankTransactions.Where(x => ids.Contains(x.TinkTransactionId)).ToDictionaryAsync(x => x.TinkTransactionId, ct);
			foreach (var source in transactions)
			{
				if (!existing.TryGetValue(source.Id, out var target))
				{
					target = new BankTransaction { Id = Guid.NewGuid(), FkBankAccountId = accountId, TinkTransactionId = source.Id };
					database.BankTransactions.Add(target);
					result.AddedTransactions++;
				}
				else
				{
					if (target.FkBankAccountId != accountId) throw new Tink.Http.TinkApiException("transactions", "invalid_response");
					if (target.Amount != source.Amount || target.CurrencyCode != source.CurrencyCode || target.Status != "Booked"
						|| target.Description != source.Description || target.Date != source.Date) result.UpdatedTransactions++;
				}
				target.Amount = source.Amount;
				target.CurrencyCode = source.CurrencyCode;
				target.Status = "Booked";
				target.Description = source.Description;
				target.Date = source.Date;
			}
			account.TransactionsSyncedThrough = result.Through;
			account.LastSuccessfulSyncAt = clock.GetUtcNow();
			result.LastSuccessfulSyncAt = account.LastSuccessfulSyncAt;
			result.Status = "SUCCEEDED";
			result.FailureCode = null;
			result.Retryable = false;
			result.RetryAfter = null;
		}, ct);
	}

	public Task FailAccountAsync(BankRefresh claimed, Guid accountId, RefreshFailure failure, CancellationToken ct) => WithLeaseAsync(claimed, async job =>
	{
		var account = await database.RefreshAccounts.SingleAsync(x => x.RefreshId == job.Id && x.AccountId == accountId, ct);
		if (account.Status == "SUCCEEDED") return;
		account.Status = "FAILED";
		account.FailureCode = failure.Code;
		account.Retryable = failure.Retryable;
		account.RetryAfter = failure.RetryAfter;
	}, ct, checkIdentity: false);

	public Task FinishAsync(BankRefresh claimed, RefreshFailure? failure, CancellationToken ct) => WithLeaseAsync(claimed, async job =>
	{
		var results = await database.RefreshAccounts.Where(x => x.RefreshId == job.Id).ToListAsync(ct);
		var retry = failure is not null ? failure.Retryable : results.Any(x => x.Status != "SUCCEEDED" && x.Retryable);
		job.FailureCode = failure?.Code;
		if (retry && job.Attempts < options.Value.MaxAttempts)
		{
			job.Status = "QUEUED";
			var delay = job.Attempts == 1 ? options.Value.FirstRetrySeconds : options.Value.SubsequentRetrySeconds;
			job.NextAttemptAt = clock.GetUtcNow().AddSeconds(delay);
			var retryAfter = results.Where(x => x.Status != "SUCCEEDED").Select(x => x.RetryAfter).Append(failure?.RetryAfter).Max();
			if (retryAfter > job.NextAttemptAt) job.NextAttemptAt = retryAfter.Value;
		}
		else
		{
			var successful = results.Count(x => x.Status == "SUCCEEDED");
			job.Status = failure is null && job.DiscoveryCompleted && successful == results.Count ? "SUCCEEDED"
				: successful > 0 ? "PARTIALLY_SUCCEEDED" : "FAILED";
			foreach (var pending in results.Where(x => x.Status == "QUEUED"))
			{
				pending.Status = "FAILED";
				pending.FailureCode = failure?.Code ?? "BANKING_REFRESH_FAILED";
			}
			job.CompletedAt = clock.GetUtcNow();
		}
		job.LeaseId = null;
		job.LeaseExpiresAt = null;
	}, ct, checkIdentity: false);

	private async Task WithLeaseAsync(BankRefresh claimed, Func<BankRefresh, Task> operation, CancellationToken ct, bool checkIdentity = true)
	{
		database.ChangeTracker.Clear();
		await using var transaction = await database.Database.BeginTransactionAsync(ct);
		var job = await database.Refreshes.FromSqlInterpolated($"SELECT * FROM banking.bank_refresh WHERE _id = {claimed.Id} FOR UPDATE").SingleAsync(ct);
		if (job.Status != "RUNNING" || job.LeaseId != claimed.LeaseId || job.LeaseExpiresAt <= clock.GetUtcNow()) throw new RefreshLeaseLostException();
		if (checkIdentity)
		{
			var user = await database.Set<ZPI4M.User.Models.User>().FromSqlInterpolated(
				$"SELECT * FROM \"user\".\"user\" WHERE _id = {job.FourMoneyUserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
			if (user is null || !user.IsActive || user.TinkUserId != job.TinkUserId) throw new RefreshIdentityException();
		}
		await operation(job);
		await database.SaveChangesAsync(ct);
		await transaction.CommitAsync(ct);
	}
}

public sealed class RefreshLeaseLostException : Exception;
public sealed class RefreshIdentityException : Exception;
public sealed record RefreshFailure(string Code, bool Retryable, DateTimeOffset? RetryAfter = null);
