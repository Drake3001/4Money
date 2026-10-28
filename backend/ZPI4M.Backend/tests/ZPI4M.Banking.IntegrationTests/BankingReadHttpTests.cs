using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Banking.ApiControllers;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories.BankAccountRepository;
using ZPI4M.Banking.Repositories.BankTransactionRepository;
using ZPI4M.Banking.Services;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.User.Database;
using ZPI4M.User.Models;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Repositories.UserTokenRepository;
using ZPI4M.User.Services.Authentication;
using ZPI4M.User.Services.BankingProfile;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class BankingReadHttpTests : PostgresFixture
{
	private const string SigningKey = "synthetic-banking-read-signing-key-32-characters";
	private UserDbContext Users() => new(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
	private BankingDbContext Database() => new(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(ConnectionString).Options);

	private WebApplication Application()
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["UserAuthentication:SigningKey"] = SigningKey });
		builder.Services.AddControllers().AddApplicationPart(typeof(BankingReadController).Assembly);
		builder.Services.AddUserAuthentication(builder.Configuration);
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
		builder.Services.AddDbContext<BankingDbContext>(options => options.UseNpgsql(ConnectionString));
		builder.Services.AddDbContext<UserDbContext>(options => options.UseNpgsql(ConnectionString));
		builder.Services.AddScoped<IUserRepository, UserRepository>();
		builder.Services.AddScoped<IUserTokenRepository, UserTokenRepository>();
		builder.Services.AddSingleton<IPasswordHasher<ZPI4M.User.Models.User>, PasswordHasher<ZPI4M.User.Models.User>>();
		builder.Services.AddSingleton<IUserTokenGenerator, UserTokenGenerator>();
		builder.Services.AddScoped<IUserBankingProfileService, UserBankingProfileService>();
		builder.Services.AddScoped<IBankAccountRepository, BankAccountRepository>();
		builder.Services.AddScoped<IBankTransactionRepository, BankTransactionRepository>();
		builder.Services.AddScoped<IBankingReadService, BankingReadService>();
		// No Tink client, options, refresh worker or test authentication bypass is registered.
		var app = builder.Build();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers();
		return app;
	}

	private async Task AuthenticateAsync(HttpClient client, Guid userId)
	{
		var now = DateTime.UtcNow;
		var sessionId = Guid.NewGuid();
		await using var users = Users();
		await users.Users.Where(x => x.Id == userId).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsEmailConfirmed, true));
		users.UserTokens.Add(new UserToken
		{
			Id = sessionId,
			FkUserId = userId,
			TokenHash = new UserTokenGenerator().HashToken(sessionId.ToString()),
			Purpose = UserTokenPurpose.RefreshToken,
			CreatedAt = now,
			ExpiresAt = now.AddHours(1)
		});
		await users.SaveChangesAsync();
		var token = new AccessTokenGenerator(Options.Create(new AuthenticationOptions { SigningKey = SigningKey }))
			.Generate(userId, sessionId, now, now.AddHours(1)).Token;
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
	}

	[PostgresFact]
	public async Task JwtOwnerCanReadHistoryFilterPagesAndExactAmountsWithoutTink()
	{
		var owner = await AddUserAsync();
		var other = await AddUserAsync();
		var account = new BankAccount
		{
			Id = Guid.NewGuid(),
			FkUserId = owner,
			Name = "A history",
			CurrencyCode = "PLN",
			Type = "CHECKING",
			IsClosed = true,
			TinkUserId = "old-synthetic",
			TinkAccountId = "old-account",
			CredentialsId = "private-credential"
		};
		var emptyAccount = new BankAccount { Id = Guid.NewGuid(), FkUserId = owner, Name = "B legacy", CurrencyCode = "EUR" };
		var foreignAccount = new BankAccount { Id = Guid.NewGuid(), FkUserId = other, Name = "Other", CurrencyCode = "PLN" };
		var first = new BankTransaction
		{
			Id = Guid.NewGuid(),
			FkBankAccountId = account.Id,
			Amount = -123.45m,
			CurrencyCode = "PLN",
			Date = new(2026, 1, 10),
			Description = "Zakupy TEST",
			Status = "Booked",
			TinkTransactionId = "private-tx-1"
		};
		var second = new BankTransaction
		{
			Id = Guid.NewGuid(),
			FkBankAccountId = account.Id,
			Amount = 9999999999999999.99m,
			CurrencyCode = "PLN",
			Date = new(2026, 1, 9),
			Description = null,
			Status = "Pending",
			TinkTransactionId = "private-tx-2"
		};
		var foreignTransaction = new BankTransaction
		{
			Id = Guid.NewGuid(),
			FkBankAccountId = foreignAccount.Id,
			Amount = 1m,
			CurrencyCode = "PLN",
			Date = new(2026, 1, 10),
			Status = "Booked",
			TinkTransactionId = "private-tx-3"
		};
		await using (var db = Database())
		{
			db.BankAccounts.AddRange(account, emptyAccount, foreignAccount);
			db.BankTransactions.AddRange(first, second, foreignTransaction);
			await db.SaveChangesAsync();
		}
		await using var app = Application();
		await app.StartAsync();
		using var client = app.GetTestClient();
		using (var denied = await client.GetAsync("/api/banking/accounts")) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
		client.DefaultRequestHeaders.Add("X-Test-User", owner.ToString());
		using (var denied = await client.GetAsync("/api/banking/accounts")) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
		client.DefaultRequestHeaders.Remove("X-Test-User");
		await AuthenticateAsync(client, owner);
		using (var accounts = await GetAsync(client, "/api/banking/accounts?userId=" + other))
		{
			Assert.Equal(2, accounts.RootElement.GetProperty("totalCount").GetInt32());
			Assert.Equal(account.Id, accounts.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
			Assert.True(accounts.RootElement.GetProperty("items")[0].GetProperty("isClosed").GetBoolean());
		}
		using (var detail = await GetAsync(client, "/api/banking/accounts/" + account.Id))
			Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("iban").ValueKind);
		using (var filteredAccounts = await GetAsync(client, "/api/banking/accounts?name=HISTORY&currencyCode=pln&type=CHECKING&isClosed=true"))
			Assert.Equal(1, filteredAccounts.RootElement.GetProperty("totalCount").GetInt32());
		using (var transactions = await GetAsync(client, "/api/banking/transactions?pageSize=1"))
		{
			Assert.Equal(2, transactions.RootElement.GetProperty("totalCount").GetInt32());
			Assert.True(transactions.RootElement.GetProperty("hasNextPage").GetBoolean());
			Assert.Equal("-123.45", transactions.RootElement.GetProperty("items")[0].GetProperty("amount").GetString());
		}
		using (var page = await GetAsync(client, "/api/banking/transactions?pageSize=1&pageNumber=2"))
			Assert.Equal("9999999999999999.99", page.RootElement.GetProperty("items")[0].GetProperty("amount").GetString());
		using (var detail = await GetAsync(client, "/api/banking/transactions/" + second.Id))
			Assert.Equal("9999999999999999.99", detail.RootElement.GetProperty("amount").GetString());
		using (var filtered = await GetAsync(client, "/api/banking/transactions?bankAccountId=" + account.Id + "&dateFrom=2026-01-10&dateTo=2026-01-10&minAmount=-123.45&maxAmount=-123.45&description=test&status=Booked&currencyCode=pln"))
			Assert.Equal(first.Id, Assert.Single(filtered.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
		foreach (var route in new[] { "/api/banking/transactions?bankAccountId=" + emptyAccount.Id, "/api/banking/accounts?pageNumber=100" })
		{
			using var empty = await GetAsync(client, route);
			Assert.Empty(empty.RootElement.GetProperty("items").EnumerateArray());
		}
		foreach (var id in new[] { foreignAccount.Id, Guid.NewGuid() })
		{
			using var detail = await client.GetAsync("/api/banking/accounts/" + id);
			Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
			using var filtered = await client.GetAsync("/api/banking/transactions?bankAccountId=" + id);
			Assert.Equal(HttpStatusCode.NotFound, filtered.StatusCode);
		}
		foreach (var id in new[] { foreignTransaction.Id, Guid.NewGuid() })
		{
			using var detail = await client.GetAsync("/api/banking/transactions/" + id);
			Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
		}
		Assert.Equal(0, Tink.CreateCalls);
	}

	[PostgresFact]
	public async Task RevokedSessionIsRejectedByProductionJwtHandler()
	{
		var owner = await AddUserAsync();
		await using var app = Application();
		await app.StartAsync();
		using var client = app.GetTestClient();
		await AuthenticateAsync(client, owner);
		using (var empty = await GetAsync(client, "/api/banking/transactions"))
			Assert.Equal(0, empty.RootElement.GetProperty("totalCount").GetInt32());
		await using var users = Users();
		await users.UserTokens.Where(x => x.FkUserId == owner).ExecuteDeleteAsync();
		using var denied = await client.GetAsync("/api/banking/transactions");
		Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
	}

	private static async Task<JsonDocument> GetAsync(HttpClient client, string route)
	{
		using var response = await client.GetAsync(route);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
		var text = await response.Content.ReadAsStringAsync();
		foreach (var field in new[] { "tinkUserId", "tinkAccountId", "tinkTransactionId", "credentialsId", "private-" })
			Assert.DoesNotContain(field, text);
		return JsonDocument.Parse(text);
	}
}
