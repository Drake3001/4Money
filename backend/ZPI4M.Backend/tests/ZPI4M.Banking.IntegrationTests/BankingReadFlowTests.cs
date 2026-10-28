using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories.BankAccountRepository;
using ZPI4M.Banking.Repositories.BankTransactionRepository;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.User.Database;
using ZPI4M.User.Services.BankingProfile;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class BankingReadFlowTests : PostgresFixture
{
	private BankingDbContext Database() => new(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(ConnectionString).Options);
	private UserDbContext Users() => new(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
	private static BankingReadService Service(BankingDbContext db, UserDbContext users) => new(
		new BankAccountRepository(db), new BankTransactionRepository(db), new UserBankingProfileService(users));

	private async Task<(Guid Owner, Guid Other, BankAccount[] Accounts, BankTransaction[] Transactions)> SeedAsync()
	{
		var owner = await AddUserAsync(tinkUserId: "new-identity");
		var other = await AddUserAsync();
		var accounts = new[]
		{
			new BankAccount { Id = new("10000000-0000-0000-0000-000000000001"), FkUserId = owner, Name = "Konto", CurrencyCode = "PLN",
				Type = "CHECKING", TinkUserId = "old-identity", TinkAccountId = "historical", IsClosed = true,
				LastSuccessfulSyncAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
				TransactionsSyncedThrough = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero) },
			new BankAccount { Id = new("10000000-0000-0000-0000-000000000002"), FkUserId = owner, Name = "Konto", CurrencyCode = "EUR",
				Type = "SAVINGS", Number = "masked-123", Iban = "synthetic-iban", FinancialInstitutionId = "test-bank" },
			new BankAccount { Id = new("10000000-0000-0000-0000-000000000003"), FkUserId = owner, Name = "Zapasowe", CurrencyCode = "PLN" },
			new BankAccount { Id = new("10000000-0000-0000-0000-000000000004"), FkUserId = other, Name = "Konto", CurrencyCode = "PLN" }
		};
		var transactions = new[]
		{
			Transaction("20000000-0000-0000-0000-000000000001", accounts[0], -123.45m, "Booked", new(2026, 1, 10), "Zakupy TEST"),
			Transaction("20000000-0000-0000-0000-000000000002", accounts[0], 12.34m, "Pending", new(2026, 1, 10), null),
			Transaction("20000000-0000-0000-0000-000000000003", accounts[1], 0m, "Cancelled", new(2026, 1, 9), "Zwrot"),
			Transaction("20000000-0000-0000-0000-000000000004", accounts[3], -123.45m, "Booked", new(2026, 1, 10), "Zakupy TEST")
		};
		await using var db = Database();
		db.BankAccounts.AddRange(accounts);
		db.BankTransactions.AddRange(transactions);
		await db.SaveChangesAsync();
		return (owner, other, accounts, transactions);
	}

	private static BankTransaction Transaction(string id, BankAccount account, decimal amount, string status, DateOnly date, string? description)
		=> new()
		{
			Id = new(id),
			FkBankAccountId = account.Id,
			Amount = amount,
			CurrencyCode = account.CurrencyCode,
			Status = status,
			Date = date,
			Description = description,
			TinkTransactionId = "synthetic-" + id
		};

	[PostgresFact]
	public async Task AllReadsAreOwnerScopedAndHistoryRemainsAvailableWithoutTink()
	{
		var data = await SeedAsync();
		await using var db = Database();
		await using var users = Users();
		var service = Service(db, users);
		var accounts = (await service.GetAccountsAsync(data.Owner, new())).Value!;
		Assert.Equal(data.Accounts.Take(3).Select(x => x.Id), accounts.Items.Select(x => x.Id));
		Assert.True(accounts.Items[0].IsClosed);
		Assert.Null(accounts.Items[0].Iban);
		Assert.Null(accounts.Items[0].Number);
		Assert.Null(accounts.Items[0].FinancialInstitutionId);
		Assert.Equal(data.Accounts[0].LastSuccessfulSyncAt, accounts.Items[0].LastSuccessfulSyncAt);
		Assert.Equal(data.Accounts[0].TransactionsSyncedThrough, accounts.Items[0].TransactionsSyncedThrough);
		Assert.Null(accounts.Items[1].LastSuccessfulSyncAt);
		Assert.Equal("synthetic-iban", accounts.Items[1].Iban);
		Assert.Equal("test-bank", accounts.Items[1].FinancialInstitutionId);
		Assert.Equal("masked-123", accounts.Items[1].Number);
		var transactions = (await service.GetTransactionsAsync(data.Owner, new())).Value!;
		Assert.Equal(3, transactions.TotalCount);
		Assert.Equal(new[] { "Booked", "Pending", "Cancelled" }, transactions.Items.Select(x => x.Status));
		Assert.Equal(new[] { -123.45m, 12.34m, 0m }, transactions.Items.Select(x => x.Amount));
		Assert.Null(transactions.Items[1].Description);
		Assert.Equal(new DateOnly(2026, 1, 10), transactions.Items[0].Date);
		foreach (var account in data.Accounts.Take(3))
			Assert.Equal(account.Id, (await service.GetAccountAsync(data.Owner, account.Id)).Value!.Id);
		foreach (var transaction in data.Transactions.Take(3))
			Assert.Equal(transaction.Id, (await service.GetTransactionAsync(data.Owner, transaction.Id)).Value!.Id);
		Assert.Single((await service.GetAccountsAsync(data.Other, new())).Value!.Items);
		Assert.Single((await service.GetTransactionsAsync(data.Other, new())).Value!.Items);
		Assert.Empty(db.ChangeTracker.Entries());
		Assert.Empty(users.ChangeTracker.Entries());
		Assert.Equal(0, await db.SaveChangesAsync());
		Assert.Equal(0, await users.SaveChangesAsync());
		Assert.Equal(0, Tink.CreateCalls);
	}

	[PostgresFact]
	public async Task ForeignAndMissingDetailsAndAccountFiltersAreIndistinguishable()
	{
		var data = await SeedAsync();
		await using var db = Database();
		await using var users = Users();
		var service = Service(db, users);
		foreach (var id in new[] { data.Accounts[3].Id, Guid.NewGuid() })
		{
			Assert.Equal(BankingErrorCode.NotFound, (await service.GetAccountAsync(data.Owner, id)).Error);
			Assert.Equal(BankingErrorCode.NotFound, (await service.GetTransactionsAsync(data.Owner, new() { BankAccountId = id })).Error);
		}
		foreach (var id in new[] { data.Transactions[3].Id, Guid.NewGuid() })
			Assert.Equal(BankingErrorCode.NotFound, (await service.GetTransactionAsync(data.Owner, id)).Error);
		var empty = (await service.GetTransactionsAsync(data.Owner, new() { BankAccountId = data.Accounts[2].Id })).Value!;
		Assert.Empty(empty.Items);
		Assert.Equal(0, empty.TotalCount);
	}

	[PostgresFact]
	public async Task AccountFiltersPaginationAndAllSortsUseStableLocalIds()
	{
		var data = await SeedAsync();
		await using var db = Database();
		await using var users = Users();
		var service = Service(db, users);
		var filtered = (await service.GetAccountsAsync(data.Owner, new()
		{
			Name = " KON ",
			CurrencyCode = " pln ",
			Type = " CHECKING ",
			IsClosed = true
		})).Value!;
		Assert.Equal(data.Accounts[0].Id, Assert.Single(filtered.Items).Id);
		Assert.Equal(2, (await service.GetAccountsAsync(data.Owner, new() { IsClosed = false })).Value!.TotalCount);
		Assert.Equal(3, (await service.GetAccountsAsync(data.Owner, new() { Name = " ", CurrencyCode = " ", Type = " " })).Value!.TotalCount);
		foreach (var sort in Enum.GetValues<BankAccountReadSortBy>())
		{
			var key = sort switch
			{
				BankAccountReadSortBy.Name => new Func<BankAccount, string>(x => x.Name),
				BankAccountReadSortBy.CurrencyCode => x => x.CurrencyCode,
				_ => x => x.IsClosed ? "1" : "0"
			};
			foreach (var descending in new[] { false, true })
			{
				var source = data.Accounts.Take(3);
				var expected = (descending ? source.OrderByDescending(key) : source.OrderBy(key)).ThenBy(x => x.Id).Select(x => x.Id).ToArray();
				var actual = new List<Guid>();
				for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
				{
					var page = (await service.GetAccountsAsync(data.Owner, new()
					{
						SortBy = sort,
						Descending = descending,
						PageNumber = pageNumber,
						PageSize = 1
					})).Value!;
					Assert.Equal(3, page.TotalCount);
					Assert.Equal(3, page.TotalPages);
					Assert.Equal(pageNumber < 3, page.HasNextPage);
					Assert.Equal(pageNumber > 1, page.HasPreviousPage);
					actual.Add(Assert.Single(page.Items).Id);
				}
				Assert.Equal(expected, actual);
			}
		}
		var beyond = (await service.GetAccountsAsync(data.Owner, new() { PageNumber = 9, PageSize = 1 })).Value!;
		Assert.Empty(beyond.Items);
		Assert.Equal(3, beyond.TotalCount);
	}

	[PostgresFact]
	public async Task TransactionFiltersAreInclusiveAndPreserveSignedAmounts()
	{
		var data = await SeedAsync();
		await using var db = Database();
		await using var users = Users();
		var service = Service(db, users);
		var exact = (await service.GetTransactionsAsync(data.Owner, new()
		{
			BankAccountId = data.Accounts[0].Id,
			DateFrom = new(2026, 1, 10),
			DateTo = new(2026, 1, 10),
			MinAmount = -123.45m,
			MaxAmount = -123.45m,
			CurrencyCode = " pln ",
			Description = " TEST ",
			Status = " Booked "
		})).Value!;
		Assert.Equal(data.Transactions[0].Id, Assert.Single(exact.Items).Id);
		Assert.Equal(2, (await service.GetTransactionsAsync(data.Owner, new() { BankAccountId = data.Accounts[0].Id })).Value!.TotalCount);
		var positive = (await service.GetTransactionsAsync(data.Owner, new() { MinAmount = 12.34m, MaxAmount = 12.34m })).Value!;
		Assert.Equal(12.34m, Assert.Single(positive.Items).Amount);
		Assert.Equal("EUR", Assert.Single((await service.GetTransactionsAsync(data.Owner, new() { CurrencyCode = "eur" })).Value!.Items).CurrencyCode);
		Assert.Empty((await service.GetTransactionsAsync(data.Owner, new() { Status = "booked" })).Value!.Items);
		Assert.Empty((await service.GetAccountsAsync(data.Owner, new() { Type = "checking" })).Value!.Items);
		Assert.Equal(3, (await service.GetTransactionsAsync(data.Owner, new() { Description = " ", Status = " ", CurrencyCode = " " })).Value!.TotalCount);
		Assert.Empty((await service.GetTransactionsAsync(data.Owner, new() { DateFrom = new(2026, 1, 11) })).Value!.Items);
	}

	[PostgresFact]
	public async Task TransactionSortsAndPagesAreStableInBothDirections()
	{
		var data = await SeedAsync();
		await using var db = Database();
		await using var users = Users();
		var service = Service(db, users);
		foreach (var sort in Enum.GetValues<BankTransactionReadSortBy>())
		{
			Func<BankTransaction, IComparable> key = sort switch
			{
				BankTransactionReadSortBy.Date => x => x.Date,
				BankTransactionReadSortBy.Amount => x => x.Amount,
				BankTransactionReadSortBy.Status => x => x.Status,
				_ => x => x.CurrencyCode
			};
			foreach (var descending in new[] { false, true })
			{
				var source = data.Transactions.Take(3);
				var expected = (descending ? source.OrderByDescending(key) : source.OrderBy(key)).ThenBy(x => x.Id).Select(x => x.Id).ToArray();
				var actual = new List<Guid>();
				for (var number = 1; number <= 3; number++)
				{
					var page = (await service.GetTransactionsAsync(data.Owner, new()
					{
						SortBy = sort,
						Descending = descending,
						PageNumber = number,
						PageSize = 1
					})).Value!;
					Assert.Equal(3, page.TotalCount);
					actual.Add(Assert.Single(page.Items).Id);
				}
				Assert.Equal(expected, actual);
			}
		}
		Assert.Empty((await service.GetTransactionsAsync(data.Owner, new() { PageNumber = 9, PageSize = 1 })).Value!.Items);
	}

	[PostgresFact]
	public async Task EmptyHistoryNeedsNoTinkMappingAndUnavailableUsersCannotRead()
	{
		var owner = await AddUserAsync();
		var inactive = await AddUserAsync(active: false);
		await using var db = Database();
		await using var users = Users();
		var service = Service(db, users);
		Assert.Empty((await service.GetAccountsAsync(owner, new())).Value!.Items);
		Assert.Empty((await service.GetTransactionsAsync(owner, new())).Value!.Items);
		foreach (var id in new[] { inactive, Guid.NewGuid() })
		{
			var expected = id == inactive ? BankingErrorCode.Forbidden : BankingErrorCode.NotFound;
			Assert.Equal(expected, (await service.GetAccountsAsync(id, new())).Error);
			Assert.Equal(expected, (await service.GetTransactionsAsync(id, new())).Error);
			Assert.Equal(expected, (await service.GetAccountAsync(id, Guid.NewGuid())).Error);
			Assert.Equal(expected, (await service.GetTransactionAsync(id, Guid.NewGuid())).Error);
		}
		Assert.Empty(db.ChangeTracker.Entries());
	}
}
