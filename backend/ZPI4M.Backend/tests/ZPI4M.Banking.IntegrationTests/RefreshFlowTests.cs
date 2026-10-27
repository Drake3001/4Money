using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories;
using ZPI4M.Banking.Services.Refresh;
using ZPI4M.Banking.Tink;
using ZPI4M.Banking.Tink.Http;
using ZPI4M.User.Database;
using ZPI4M.User.Services;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class RefreshFlowTests : PostgresFixture
{
	// PostgreSQL timestamps have microsecond precision.
	public RefreshFlowTests() => Clock.Advance(TimeSpan.FromTicks(-(Clock.GetUtcNow().Ticks % 10)));
	private BankingDbContext Database() => new(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(ConnectionString).Options);
	private BankRefreshRepository Repository(BankingDbContext db, BankRefreshOptions? settings = null) => new(db, Clock, Options.Create(settings ?? new()));
	private async Task Process(BankRefresh job, Data data, BankRefreshOptions? settings = null)
	{
		await using var db = Database();
		await using var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		await new BankRefreshProcessor(Repository(db, settings), data, new UserBankingProfileService(users), Clock,
			Options.Create(settings ?? new())).ProcessAsync(job, default);
	}

	[PostgresFact]
	public async Task ImportIsIdempotentUpdatesInPlaceAndUsesPerAccountWindows()
	{
		var user = await AddUserAsync(tinkUserId: "owner");
		var data = new Data();
		await using var db = Database();
		var repo = Repository(db);
		var first = await repo.EnqueueAsync(user, "owner", default);
		Assert.Equal(first.Id, (await repo.EnqueueAsync(user, "owner", default)).Id);
		var claimed = (await repo.ClaimAsync(default))!;
		await Process(claimed, data);
		var finished = (await repo.GetAsync(user, first.Id, default))!;
		Assert.Equal("SUCCEEDED", finished.Status);
		Assert.Equal(2, finished.Accounts.Count);
		Assert.All(data.Windows, x => Assert.Equal(claimed.StartedAt!.Value.AddDays(-30), x.From));
		var stored = await db.BankTransactions.AsNoTracking().SingleAsync(x => x.TinkTransactionId == "tx-a");
		var oldId = stored.Id;
		Assert.Equal(-12.34m, stored.Amount);
		var accountA = await db.BankAccounts.AsNoTracking().SingleAsync(x => x.TinkAccountId == "a");
		Assert.Null(accountA.Iban);
		Assert.Null(accountA.CredentialsId);
		Assert.Equal(claimed.StartedAt, accountA.TransactionsSyncedThrough);
		Clock.Advance(TimeSpan.FromDays(60));
		data.Amount = -20m;
		data.IncludeC = true;
		data.Windows.Clear();
		await repo.EnqueueAsync(user, "owner", default);
		var next = (await repo.ClaimAsync(default))!;
		await Process(next, data);
		Assert.All(data.Windows.Where(x => x.Account != "c"), x => Assert.Equal(claimed.StartedAt!.Value.AddDays(-7), x.From));
		Assert.Equal(next.StartedAt!.Value.AddDays(-30), data.Windows.Single(x => x.Account == "c").From);
		stored = await db.BankTransactions.AsNoTracking().SingleAsync(x => x.TinkTransactionId == "tx-a");
		Assert.Equal(oldId, stored.Id);
		Assert.Equal(-20m, stored.Amount);
		var repeated = (await repo.GetAsync(user, next.Id, default))!;
		Assert.Equal(0, repeated.Accounts.Sum(x => x.AddedTransactions));
		Assert.Equal(1, repeated.Accounts.Sum(x => x.UpdatedTransactions));
	}

	[PostgresFact]
	public async Task PartialFailurePreservesSuccessfulAccountAndRetriesOnlyFailedAccount()
	{
		var user = await AddUserAsync(tinkUserId: "owner");
		var data = new Data { FailB = new TinkApiException("data", "upstream_error", HttpStatusCode.TooManyRequests, TimeSpan.FromMinutes(5)) };
		await using var db = Database();
		var repo = Repository(db);
		await repo.EnqueueAsync(user, "owner", default);
		var claimed = (await repo.ClaimAsync(default))!;
		await Process(claimed, data);
		var queued = (await repo.GetAsync(user, claimed.Id, default))!;
		Assert.Equal("QUEUED", queued.Status);
		Assert.True(queued.NextAttemptAt >= Clock.GetUtcNow().AddMinutes(5));
		Assert.Equal("SUCCEEDED", queued.Accounts.Single(x => x.TinkAccountId == "a").Status);
		Assert.Null((await db.BankAccounts.AsNoTracking().SingleAsync(x => x.TinkAccountId == "b")).LastSuccessfulSyncAt);
		Assert.Null(await repo.ClaimAsync(default));
		Clock.Advance(TimeSpan.FromMinutes(6));
		data.FailB = null;
		var retry = (await repo.ClaimAsync(default))!;
		await Process(retry, data);
		Assert.Equal("SUCCEEDED", (await repo.GetAsync(user, retry.Id, default))!.Status);
		Assert.Equal(1, data.Discoveries);
		Assert.Equal(1, data.Windows.Count(x => x.Account == "a"));
		Assert.Equal(2, data.Windows.Count(x => x.Account == "b"));
	}

	[PostgresFact]
	public async Task PermanentAccountFailureProducesPartialSuccessWithoutMovingItsCursor()
	{
		var user = await AddUserAsync(tinkUserId: "owner");
		await using var db = Database();
		var repo = Repository(db);
		await repo.EnqueueAsync(user, "owner", default);
		var job = (await repo.ClaimAsync(default))!;
		await Process(job, new Data { FailB = new TinkApiException("data", "forbidden", HttpStatusCode.Forbidden) });
		var result = (await repo.GetAsync(user, job.Id, default))!;
		Assert.Equal("PARTIALLY_SUCCEEDED", result.Status);
		Assert.Equal("BANKING_TINK_ACCESS_DENIED", result.Accounts.Single(x => x.TinkAccountId == "b").FailureCode);
		Assert.Null((await db.BankAccounts.AsNoTracking().SingleAsync(x => x.TinkAccountId == "b")).TransactionsSyncedThrough);
		Assert.Single(await db.BankTransactions.ToListAsync());
	}

	[PostgresFact]
	public async Task MissingAccountsAreClosedAndReopenedWithoutDeletingHistoryOrLegacyRows()
	{
		var user = await AddUserAsync(tinkUserId: "owner");
		await using var db = Database();
		db.BankAccounts.Add(new BankAccount { Id = Guid.NewGuid(), FkUserId = user, Name = "Legacy", CurrencyCode = "PLN" });
		await db.SaveChangesAsync();
		var repo = Repository(db);
		var data = new Data();
		await repo.EnqueueAsync(user, "owner", default);
		await Process((await repo.ClaimAsync(default))!, data);
		var original = await db.BankAccounts.AsNoTracking().SingleAsync(x => x.TinkAccountId == "a");
		data.IncludeA = false;
		await repo.EnqueueAsync(user, "owner", default);
		await Process((await repo.ClaimAsync(default))!, data);
		Assert.True((await db.BankAccounts.AsNoTracking().SingleAsync(x => x.Id == original.Id)).IsClosed);
		Assert.False((await db.BankAccounts.AsNoTracking().SingleAsync(x => x.Name == "Legacy")).IsClosed);
		Assert.Single(await db.BankTransactions.Where(x => x.FkBankAccountId == original.Id).ToListAsync());
		data.IncludeA = true;
		await repo.EnqueueAsync(user, "owner", default);
		await Process((await repo.ClaimAsync(default))!, data);
		Assert.False((await db.BankAccounts.AsNoTracking().SingleAsync(x => x.Id == original.Id)).IsClosed);
	}

	[PostgresFact]
	public async Task LeaseRecoveryFencesOldWorkerAndDoesNotExposeForeignJobs()
	{
		var user = await AddUserAsync(tinkUserId: "owner");
		await using var firstDb = Database();
		await using var secondDb = Database();
		var firstRepo = Repository(firstDb);
		var secondRepo = Repository(secondDb);
		await firstRepo.EnqueueAsync(user, "owner", default);
		var first = (await firstRepo.ClaimAsync(default))!;
		Assert.Null(await secondRepo.ClaimAsync(default));
		Assert.Null(await secondRepo.GetAsync(Guid.NewGuid(), first.Id, default));
		Clock.Advance(TimeSpan.FromMinutes(3));
		var replacement = (await secondRepo.ClaimAsync(default))!;
		Assert.NotEqual(first.LeaseId, replacement.LeaseId);
		Assert.False(await firstRepo.RenewAsync(first.Id, first.LeaseId!.Value, default));
		await Assert.ThrowsAsync<RefreshLeaseLostException>(() => firstRepo.DiscoverAsync(first, [], default));
		await Process(replacement, new Data());
		Assert.Equal("SUCCEEDED", (await secondRepo.GetAsync(user, first.Id, default))!.Status);
	}

	[PostgresFact]
	public async Task ConcurrentEnqueueReturnsOneDurableJob()
	{
		var user = await AddUserAsync(tinkUserId: "owner");
		await using var first = Database();
		await using var second = Database();
		var jobs = await Task.WhenAll(Repository(first).EnqueueAsync(user, "owner", default), Repository(second).EnqueueAsync(user, "owner", default));
		Assert.Equal(jobs[0].Id, jobs[1].Id);
		Assert.Equal(1, await first.Refreshes.CountAsync());
	}

	[PostgresFact]
	public async Task DiscoveryFailureDoesNotCloseAccountsAndMappingChangePreventsTransactionWrite()
	{
		var user = await AddUserAsync(tinkUserId: "owner");
		await using var db = Database();
		var repo = Repository(db);
		await repo.EnqueueAsync(user, "owner", default);
		var first = (await repo.ClaimAsync(default))!;
		await Process(first, new Data());
		await repo.EnqueueAsync(user, "owner", default);
		var failed = (await repo.ClaimAsync(default))!;
		await Process(failed, new Data { FailDiscovery = true });
		Assert.All(await db.BankAccounts.AsNoTracking().ToListAsync(), x => Assert.False(x.IsClosed));
		Assert.Equal("FAILED", (await repo.GetAsync(user, failed.Id, default))!.Status);
		await repo.EnqueueAsync(user, "owner", default);
		var changed = (await repo.ClaimAsync(default))!;
		await using (var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options))
			await users.Users.Where(x => x.Id == user).ExecuteUpdateAsync(s => s.SetProperty(x => x.TinkUserId, "different"));
		await Process(changed, new Data());
		Assert.Equal("BANKING_IDENTITY_CHANGED", (await repo.GetAsync(user, changed.Id, default))!.FailureCode);
	}


	[PostgresFact]
	public async Task RetryLimitTerminatesJobAndKeepsSuccessfulAccountCommitted()
	{
		var user = await AddUserAsync(tinkUserId: "owner");
		await using var db = Database();
		var repo = Repository(db);
		await repo.EnqueueAsync(user, "owner", default);
		var data = new Data { FailB = new TinkApiException("data", "upstream_error", HttpStatusCode.ServiceUnavailable) };
		BankRefresh? last = null;
		for (var attempt = 0; attempt < 3; attempt++)
		{
			last = (await repo.ClaimAsync(default))!;
			await Process(last, data);
			Clock.Advance(TimeSpan.FromMinutes(3));
		}
		var result = (await repo.GetAsync(user, last!.Id, default))!;
		Assert.Equal("PARTIALLY_SUCCEEDED", result.Status);
		Assert.Equal(3, result.Attempts);
		Assert.Null(await repo.ClaimAsync(default));
		Assert.Equal(1, data.Windows.Count(x => x.Account == "a"));
		Assert.Equal(3, data.Windows.Count(x => x.Account == "b"));
		Assert.Single(await db.BankTransactions.AsNoTracking().ToListAsync());
	}

	private sealed class Data : ITinkDataGateway, ITinkDataSession
	{
		public int Discoveries { get; private set; }
		public bool IncludeA { get; set; } = true;
		public bool IncludeC { get; set; }
		public bool FailDiscovery { get; set; }
		public decimal Amount { get; set; } = -12.34m;
		public TinkApiException? FailB { get; set; }
		public List<(string Account, DateTimeOffset From, DateTimeOffset Through)> Windows { get; } = [];
		public Task<ITinkDataSession> OpenAsync(string user, CancellationToken ct) => Task.FromResult<ITinkDataSession>(this);
		public Task<IReadOnlyList<TinkBankAccount>> GetAccountsAsync(CancellationToken ct)
		{
			Discoveries++;
			if (FailDiscovery) throw new TinkApiException("data", "invalid_response");
			return Task.FromResult<IReadOnlyList<TinkBankAccount>>((IncludeA ? new[] { "a", "b" } : ["b"]).Concat(IncludeC ? new[] { "c" } : [])
				.Select(id => new TinkBankAccount(id, "Synthetic " + id, "TEST ** 0001", null, null, "CHECKING", "PLN")).ToArray());
		}
		public Task<IReadOnlyList<TinkBankTransaction>> GetTransactionsAsync(string accountId, DateTimeOffset from, DateTimeOffset through, CancellationToken ct)
		{
			Windows.Add((accountId, from, through));
			if (accountId == "b" && FailB is not null) throw FailB;
			return Task.FromResult<IReadOnlyList<TinkBankTransaction>>(accountId == "a"
				? [new("tx-a", "a", Amount, "PLN", "BOOKED", "Synthetic purchase", DateOnly.FromDateTime(through.UtcDateTime))] : []);
		}
		public void Dispose() { }
	}
}
