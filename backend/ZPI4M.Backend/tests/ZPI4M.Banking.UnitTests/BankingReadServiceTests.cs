using Xunit;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories.BankAccountRepository;
using ZPI4M.Banking.Repositories.BankTransactionRepository;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Services;

namespace ZPI4M.Banking.UnitTests;

public sealed class BankingReadServiceTests
{
	[Fact]
	public async Task MapsAccountSnapshotAndPassesOwnerAndFiltersWithoutChangingQuery()
	{
		var dependencies = new Dependencies();
		var account = dependencies.Accounts.Account!;
		var query = new BankAccountReadQuery
		{
			Name = " name ",
			CurrencyCode = " pln ",
			Type = " CHECKING ",
			IsClosed = true,
			SortBy = BankAccountReadSortBy.IsClosed,
			Descending = true,
			PageNumber = 2,
			PageSize = 5
		};
		var result = (await dependencies.Service.GetAccountsAsync(dependencies.UserId, query)).Value!;
		Assert.Equal(7, result.TotalCount);
		Assert.Equal(2, result.PageNumber);
		Assert.Equal(5, result.PageSize);
		var item = Assert.Single(result.Items);
		Assert.Equal(new BankAccountDetails(account.Id, account.Name, account.Number, account.Iban,
			account.FinancialInstitutionId, account.Type, account.CurrencyCode, account.IsClosed,
			account.LastSuccessfulSyncAt, account.TransactionsSyncedThrough), item);
		var passed = dependencies.Accounts.Query!;
		Assert.Equal(dependencies.UserId, passed.UserId);
		Assert.Equal(query.Name, passed.Name);
		Assert.Equal(query.CurrencyCode, passed.CurrencyCode);
		Assert.Equal(query.Type, passed.Type);
		Assert.True(passed.IsClosed);
		Assert.Equal(BankAccountSortBy.IsClosed, passed.SortBy);
		Assert.True(passed.Descending);
		Assert.Equal(2, passed.PageNumber);
		Assert.Equal(5, passed.PageSize);
		Assert.Equal(" name ", query.Name);
	}

	[Fact]
	public async Task MapsTransactionAndPassesAllFiltersAndOwner()
	{
		var dependencies = new Dependencies();
		var query = new BankTransactionReadQuery
		{
			BankAccountId = dependencies.Accounts.Account!.Id,
			DateFrom = new(2026, 1, 1),
			DateTo = new(2026, 2, 1),
			MinAmount = -100.01m,
			MaxAmount = 200.02m,
			CurrencyCode = "pln",
			Description = "test",
			Status = "Pending",
			SortBy = BankTransactionReadSortBy.Amount,
			Descending = false,
			PageNumber = 3,
			PageSize = 4
		};
		var page = (await dependencies.Service.GetTransactionsAsync(dependencies.UserId, query)).Value!;
		Assert.Equal(7, page.TotalCount);
		Assert.Equal(3, page.PageNumber);
		Assert.Equal(4, page.PageSize);
		var transaction = dependencies.Transactions.Transaction!;
		Assert.Equal(new BankTransactionDetails(transaction.Id, transaction.FkBankAccountId, transaction.Amount,
			transaction.CurrencyCode, transaction.Date, transaction.Description, transaction.Status), Assert.Single(page.Items));
		var passed = dependencies.Transactions.Query!;
		Assert.Equal(dependencies.UserId, passed.UserId);
		Assert.Equal(query.BankAccountId, passed.BankAccountId);
		Assert.Equal(query.DateFrom, passed.DateFrom);
		Assert.Equal(query.DateTo, passed.DateTo);
		Assert.Equal(query.MinAmount, passed.MinAmount);
		Assert.Equal(query.MaxAmount, passed.MaxAmount);
		Assert.Equal(query.CurrencyCode, passed.CurrencyCode);
		Assert.Equal(query.Description, passed.Description);
		Assert.Equal(query.Status, passed.Status);
		Assert.Equal(BankTransactionSortBy.Amount, passed.SortBy);
		Assert.False(passed.Descending);
		Assert.Equal(3, passed.PageNumber);
		Assert.Equal(4, passed.PageSize);
		Assert.Equal((dependencies.UserId, query.BankAccountId.Value), dependencies.Accounts.DetailRequest);
	}

	[Fact]
	public async Task DetailsUseScopedRepositoryMethodsAndMissingResourcesReturnNotFound()
	{
		var dependencies = new Dependencies();
		var accountId = dependencies.Accounts.Account!.Id;
		var transactionId = dependencies.Transactions.Transaction!.Id;
		Assert.True((await dependencies.Service.GetAccountAsync(dependencies.UserId, accountId)).IsSuccess);
		Assert.True((await dependencies.Service.GetTransactionAsync(dependencies.UserId, transactionId)).IsSuccess);
		Assert.Equal((dependencies.UserId, accountId), dependencies.Accounts.DetailRequest);
		Assert.Equal((dependencies.UserId, transactionId), dependencies.Transactions.DetailRequest);
		dependencies.Accounts.Account = null;
		dependencies.Transactions.Transaction = null;
		Assert.Equal(BankingErrorCode.NotFound, (await dependencies.Service.GetAccountAsync(dependencies.UserId, accountId)).Error);
		Assert.Equal(BankingErrorCode.NotFound, (await dependencies.Service.GetTransactionAsync(dependencies.UserId, transactionId)).Error);
		Assert.Equal(BankingErrorCode.NotFound, (await dependencies.Service.GetTransactionsAsync(dependencies.UserId,
			new() { BankAccountId = accountId })).Error);
		Assert.Null(dependencies.Transactions.Query);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task UnavailableUsersCannotReadAnyResources(bool missing)
	{
		var dependencies = new Dependencies();
		dependencies.Profile = missing ? null : dependencies.Profile! with { IsActive = false };
		var expected = missing ? BankingErrorCode.NotFound : BankingErrorCode.Forbidden;
		Assert.Equal(expected, (await dependencies.Service.GetAccountsAsync(dependencies.UserId, new())).Error);
		Assert.Equal(expected, (await dependencies.Service.GetAccountAsync(dependencies.UserId, Guid.NewGuid())).Error);
		Assert.Equal(expected, (await dependencies.Service.GetTransactionsAsync(dependencies.UserId, new())).Error);
		Assert.Equal(expected, (await dependencies.Service.GetTransactionAsync(dependencies.UserId, Guid.NewGuid())).Error);
		Assert.Null(dependencies.Accounts.Query);
		Assert.Null(dependencies.Accounts.DetailRequest);
		Assert.Null(dependencies.Transactions.Query);
		Assert.Null(dependencies.Transactions.DetailRequest);
	}

	public static IEnumerable<object[]> InvalidQueries()
	{
		yield return [new BankAccountReadQuery { PageNumber = 0 }];
		yield return [new BankAccountReadQuery { PageSize = 0 }];
		yield return [new BankAccountReadQuery { PageSize = 101 }];
		yield return [new BankAccountReadQuery { PageNumber = int.MaxValue, PageSize = 100 }];
		yield return [new BankAccountReadQuery { SortBy = (BankAccountReadSortBy)99 }];
		yield return [new BankTransactionReadQuery { PageNumber = 0 }];
		yield return [new BankTransactionReadQuery { PageSize = 101 }];
		yield return [new BankTransactionReadQuery { SortBy = (BankTransactionReadSortBy)99 }];
		yield return [new BankTransactionReadQuery { BankAccountId = Guid.Empty }];
		yield return [new BankTransactionReadQuery { DateFrom = new(2026, 2, 1), DateTo = new(2026, 1, 1) }];
		yield return [new BankTransactionReadQuery { MinAmount = 1m, MaxAmount = -1m }];
	}

	[Theory]
	[MemberData(nameof(InvalidQueries))]
	public async Task InvalidQueriesFailBeforeReadingUserOrResources(PagedQuery query)
	{
		var dependencies = new Dependencies();
		await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
		{
			if (query is BankAccountReadQuery accountQuery)
				await dependencies.Service.GetAccountsAsync(dependencies.UserId, accountQuery);
			else
				await dependencies.Service.GetTransactionsAsync(dependencies.UserId, (BankTransactionReadQuery)query);
		});
		Assert.Equal(0, dependencies.ProfileReads);
	}

	[Fact]
	public async Task EmptyIdsAndNullQueriesAreRejected()
	{
		var dependencies = new Dependencies();
		await Assert.ThrowsAnyAsync<ArgumentException>(() => dependencies.Service.GetAccountsAsync(Guid.Empty, new()));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => dependencies.Service.GetTransactionsAsync(Guid.Empty, new()));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => dependencies.Service.GetAccountAsync(Guid.Empty, Guid.NewGuid()));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => dependencies.Service.GetTransactionAsync(Guid.Empty, Guid.NewGuid()));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => dependencies.Service.GetAccountAsync(dependencies.UserId, Guid.Empty));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => dependencies.Service.GetTransactionAsync(dependencies.UserId, Guid.Empty));
		await Assert.ThrowsAsync<ArgumentNullException>(() => dependencies.Service.GetAccountsAsync(dependencies.UserId, null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => dependencies.Service.GetTransactionsAsync(dependencies.UserId, null!));
		Assert.Equal(0, dependencies.ProfileReads);
	}

	[Fact]
	public async Task CancellationAndDatabaseFailuresPropagate()
	{
		var dependencies = new Dependencies();
		using var source = new CancellationTokenSource();
		source.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dependencies.Service.GetAccountsAsync(dependencies.UserId, new(), source.Token));
		dependencies.Accounts.Failure = new ReadDatabaseException();
		await Assert.ThrowsAsync<ReadDatabaseException>(() => dependencies.Service.GetAccountsAsync(dependencies.UserId, new()));
	}

	private sealed class ReadDatabaseException : System.Data.Common.DbException;

	private sealed class Dependencies : IUserBankingProfileService
	{
		public Guid UserId { get; } = Guid.NewGuid();
		public UserBankingProfile? Profile { get; set; }
		public int ProfileReads { get; private set; }
		public Accounts Accounts { get; } = new();
		public Transactions Transactions { get; } = new();
		public BankingReadService Service => new(Accounts, Transactions, this);
		public Dependencies() => Profile = new(UserId, true, null, "test");
		public Task<UserBankingProfile?> GetAsync(Guid userId, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ProfileReads++;
			Assert.Equal(UserId, userId);
			return Task.FromResult(Profile);
		}
		public Task<UserBankingProfile?> EnsureTinkUserAsync(Guid userId, Func<CancellationToken, Task<string>> createOrRecover,
			CancellationToken cancellationToken) => throw new NotSupportedException();
	}

	private sealed class Accounts : IBankAccountRepository
	{
		public BankAccount? Account { get; set; } = new()
		{
			Id = Guid.NewGuid(),
			Name = "test",
			CurrencyCode = "PLN",
			IsClosed = true,
			LastSuccessfulSyncAt = DateTimeOffset.UtcNow,
			TransactionsSyncedThrough = DateTimeOffset.UtcNow.AddDays(-1)
		};
		public BankAccountQuery? Query { get; private set; }
		public (Guid User, Guid Account)? DetailRequest { get; private set; }
		public Exception? Failure { get; set; }
		public Task<BankAccount?> GetByIdForUserAsync(Guid userId, Guid bankAccountId, CancellationToken cancellationToken = default)
		{
			DetailRequest = (userId, bankAccountId);
			return Task.FromResult(Account);
		}
		public Task<PagedResult<BankAccount>> GetPageAsync(BankAccountQuery query, CancellationToken cancellationToken = default)
		{
			if (Failure is not null) throw Failure;
			Query = query;
			return Task.FromResult(new PagedResult<BankAccount>([Account!], 7, query.PageNumber, query.PageSize));
		}
		public void Add(BankAccount entity) => throw new NotSupportedException();
		public void Remove(BankAccount entity) => throw new NotSupportedException();
		public Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<BankAccount?> GetByAccountDetailsAsync(Guid userId, string institution, string number, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();
	}

	private sealed class Transactions : IBankTransactionRepository
	{
		public BankTransaction? Transaction { get; set; } = new()
		{
			Id = Guid.NewGuid(),
			FkBankAccountId = Guid.NewGuid(),
			Amount = -123.45m,
			CurrencyCode = "PLN",
			Date = new(2026, 1, 1),
			Status = "Pending"
		};
		public BankTransactionQuery? Query { get; private set; }
		public (Guid User, Guid Transaction)? DetailRequest { get; private set; }
		public Task<BankTransaction?> GetByIdForUserAsync(Guid userId, Guid bankTransactionId, CancellationToken cancellationToken = default)
		{
			DetailRequest = (userId, bankTransactionId);
			return Task.FromResult(Transaction);
		}
		public Task<PagedResult<BankTransaction>> GetPageAsync(BankTransactionQuery query, CancellationToken cancellationToken = default)
		{
			Query = query;
			return Task.FromResult(new PagedResult<BankTransaction>([Transaction!], 7, query.PageNumber, query.PageSize));
		}
		public void Add(BankTransaction entity) => throw new NotSupportedException();
		public void AddRange(IEnumerable<BankTransaction> entities) => throw new NotSupportedException();
		public void Remove(BankTransaction entity) => throw new NotSupportedException();
		public Task<BankTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<BankTransaction?> GetByTinkTransactionIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<bool> ExistsByTinkTransactionIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}
}
