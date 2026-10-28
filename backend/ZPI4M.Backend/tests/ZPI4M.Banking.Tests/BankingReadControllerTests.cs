using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Banking.ApiControllers;
using ZPI4M.Banking.Services;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Tests;

public sealed class BankingReadControllerTests
{
	private const string Id = "10000000-0000-0000-0000-000000000001";
	public static IEnumerable<object[]> Routes() => new[] { "accounts", "accounts/" + Id, "transactions", "transactions/" + Id }.Select(x => new object[] { x });

	[Theory]
	[MemberData(nameof(Routes))]
	public async Task AnonymousRequestsNeverReachService(string route)
	{
		await using var host = await Host.StartAsync();
		using var response = await host.Client.GetAsync("/api/banking/" + route);
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.Equal(0, host.Service.Calls);
	}

	[Theory]
	[MemberData(nameof(Routes))]
	public async Task OwnerComesFromIdentityAndResponsesHavePublicShape(string route)
	{
		await using var host = await Host.StartAsync();
		host.Authenticate(Id);
		using var response = await host.Client.GetAsync("/api/banking/" + route + "?userId=" + Guid.NewGuid());
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
		Assert.Equal(Guid.Parse(Id), host.Service.Owner);
		using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		var root = document.RootElement;
		var item = route.Contains('/') ? root : root.GetProperty("items")[0];
		Assert.Equal(Id, item.GetProperty("id").GetString());
		if (route.StartsWith("accounts"))
		{
			Assert.Equal(JsonValueKind.Null, item.GetProperty("iban").ValueKind);
			Assert.Equal(JsonValueKind.Null, item.GetProperty("number").ValueKind);
			Assert.Equal(JsonValueKind.Null, item.GetProperty("financialInstitutionId").ValueKind);
			Assert.Equal(JsonValueKind.Null, item.GetProperty("type").ValueKind);
			Assert.Equal(JsonValueKind.Null, item.GetProperty("transactionsSyncedThrough").ValueKind);
			Assert.Equal("2026-01-01T00:00:00+00:00", item.GetProperty("lastSuccessfulSyncAt").GetString());
			Assert.Equal(10, item.EnumerateObject().Count());
		}
		else
		{
			Assert.Equal("-123.45", item.GetProperty("amount").GetString());
			Assert.Equal("2026-01-10", item.GetProperty("date").GetString());
			Assert.Equal(JsonValueKind.Null, item.GetProperty("description").ValueKind);
			Assert.Equal(7, item.EnumerateObject().Count());
		}
		if (!route.Contains('/'))
		{
			Assert.Equal(1, root.GetProperty("totalCount").GetInt32());
			Assert.Equal(1, root.GetProperty("pageNumber").GetInt32());
			Assert.Equal(20, root.GetProperty("pageSize").GetInt32());
			Assert.Equal(1, root.GetProperty("totalPages").GetInt32());
			Assert.False(root.GetProperty("hasPreviousPage").GetBoolean());
			Assert.False(root.GetProperty("hasNextPage").GetBoolean());
		}
		if (host.Service.AccountQuery is { } accountQuery) Assert.False(accountQuery.Descending);
		if (host.Service.TransactionQuery is { } query) Assert.True(query.Descending);
	}

	[Fact]
	public async Task QueryBindingMapsEveryFilterAndNamedSortWithoutChangingDefaultsElsewhere()
	{
		await using var host = await Host.StartAsync();
		host.Authenticate(Id);
		using var account = await host.Client.GetAsync("/api/banking/accounts?name=Konto&currencyCode=pln&type=CHECKING&isClosed=true&sortBy=CURRENCYCODE&descending=true&pageNumber=2&pageSize=5");
		Assert.Equal(HttpStatusCode.OK, account.StatusCode);
		var accounts = host.Service.AccountQuery!;
		Assert.Equal("Konto", accounts.Name);
		Assert.Equal("pln", accounts.CurrencyCode);
		Assert.Equal("CHECKING", accounts.Type);
		Assert.True(accounts.IsClosed);
		Assert.True(accounts.Descending);
		Assert.Equal(BankAccountReadSortBy.CurrencyCode, accounts.SortBy);
		Assert.Equal(2, accounts.PageNumber);
		Assert.Equal(5, accounts.PageSize);
		using var transaction = await host.Client.GetAsync("/api/banking/transactions?bankAccountId=" + Id + "&dateFrom=2026-01-01&dateTo=2026-01-31&minAmount=-123.45&maxAmount=0&currencyCode=eur&description=test&status=Booked&sortBy=AMOUNT&descending=false&pageNumber=3&pageSize=7");
		Assert.Equal(HttpStatusCode.OK, transaction.StatusCode);
		var transactions = host.Service.TransactionQuery!;
		Assert.Equal(Guid.Parse(Id), transactions.BankAccountId);
		Assert.Equal(new DateOnly(2026, 1, 1), transactions.DateFrom);
		Assert.Equal(new DateOnly(2026, 1, 31), transactions.DateTo);
		Assert.Equal(-123.45m, transactions.MinAmount);
		Assert.Equal(0m, transactions.MaxAmount);
		Assert.Equal("eur", transactions.CurrencyCode);
		Assert.Equal("test", transactions.Description);
		Assert.Equal("Booked", transactions.Status);
		Assert.Equal(BankTransactionReadSortBy.Amount, transactions.SortBy);
		Assert.False(transactions.Descending);
		Assert.Equal(3, transactions.PageNumber);
		Assert.Equal(7, transactions.PageSize);
	}

	[Theory]
	[InlineData("accounts?pageNumber=0", "pageNumber")]
	[InlineData("accounts?pageNumber=-1", "pageNumber")]
	[InlineData("accounts?pageSize=101", "pageSize")]
	[InlineData("accounts?pageSize=0", "pageSize")]
	[InlineData("accounts?pageSize=-1", "pageSize")]
	[InlineData("accounts?pageNumber=2147483647&pageSize=100", "pageNumber")]
	[InlineData("accounts?sortBy=99", "sortBy")]
	[InlineData("accounts?sortBy=0", "sortBy")]
	[InlineData("accounts?sortBy=unknown", "sortBy")]
	[InlineData("accounts?isClosed=invalid", "isClosed")]
	[InlineData("transactions?sortBy=0", "sortBy")]
	[InlineData("transactions?pageSize=abc", "pageSize")]
	[InlineData("transactions?pageNumber=0", "pageNumber")]
	[InlineData("transactions?pageSize=0", "pageSize")]
	[InlineData("transactions?pageSize=101", "pageSize")]
	[InlineData("transactions?pageNumber=2147483647&pageSize=100", "pageNumber")]
	[InlineData("transactions?dateFrom=no-date", "dateFrom")]
	[InlineData("transactions?minAmount=invalid", "minAmount")]
	[InlineData("transactions?dateFrom=2026-02-01&dateTo=2026-01-01", "dateFrom")]
	[InlineData("transactions?minAmount=1&maxAmount=-1", "minAmount")]
	[InlineData("transactions?bankAccountId=invalid", "bankAccountId")]
	[InlineData("transactions?bankAccountId=00000000-0000-0000-0000-000000000000", "bankAccountId")]
	[InlineData("accounts/00000000-0000-0000-0000-000000000000", "accountId")]
	[InlineData("transactions/00000000-0000-0000-0000-000000000000", "transactionId")]
	public async Task InvalidInputReturnsFieldValidationBeforeCallingService(string route, string field)
	{
		await using var host = await Host.StartAsync();
		host.Authenticate(Id);
		using var response = await host.Client.GetAsync("/api/banking/" + route);
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		Assert.Contains(json.RootElement.GetProperty("errors").EnumerateObject(), x => x.Name.Equals(field, StringComparison.OrdinalIgnoreCase));
		Assert.Equal(0, host.Service.Calls);
	}

	[Theory]
	[InlineData("accounts/not-a-guid")]
	[InlineData("transactions/not-a-guid")]
	public async Task MalformedRouteIdIsNotFound(string route)
	{
		await using var host = await Host.StartAsync();
		host.Authenticate(Id);
		using var response = await host.Client.GetAsync("/api/banking/" + route);
		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
		Assert.Equal(0, host.Service.Calls);
	}

	[Theory]
	[InlineData(BankingErrorCode.NotFound, 404, "BANKING_NOT_FOUND")]
	[InlineData(BankingErrorCode.Forbidden, 403, "BANKING_FORBIDDEN")]
	[InlineData(BankingErrorCode.TemporarilyUnavailable, 503, "BANKING_TEMPORARILY_UNAVAILABLE")]
	public async Task ExpectedErrorsAreMappedForEveryOperation(BankingErrorCode error, int status, string code)
	{
		await using var host = await Host.StartAsync();
		host.Authenticate(Id);
		host.Service.Error = error;
		foreach (var route in Routes().Select(x => (string)x[0]))
		{
			using var response = await host.Client.GetAsync("/api/banking/" + route);
			Assert.Equal(status, (int)response.StatusCode);
			Assert.True(response.Headers.CacheControl?.NoStore);
			using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
		}
	}

	[Theory]
	[InlineData("not-a-guid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public async Task InvalidAuthenticatedIdentityCannotRead(string id)
	{
		await using var host = await Host.StartAsync();
		host.Authenticate(id);
		using var response = await host.Client.GetAsync("/api/banking/accounts");
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.Equal(0, host.Service.Calls);
	}

	[Theory]
	[InlineData("-123.45")]
	[InlineData("0.00")]
	[InlineData("9999999999999999.99")]
	public async Task AmountsAreExactInvariantTextInListsAndDetails(string amount)
	{
		await using var host = await Host.StartAsync();
		host.Authenticate(Id);
		host.Service.Amount = decimal.Parse(amount, CultureInfo.InvariantCulture);
		foreach (var route in new[] { "transactions", "transactions/" + Id })
		{
			using var response = await host.Client.GetAsync("/api/banking/" + route);
			using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			var value = route.Contains('/') ? json.RootElement : json.RootElement.GetProperty("items")[0];
			Assert.Equal(amount, value.GetProperty("amount").GetString());
		}
	}

	[Fact]
	public async Task OnlyDatabaseFailuresAreTranslatedAndCancellationPropagates()
	{
		var service = new ReadStub();
		var controller = new BankingReadController(service, new FixedUser())
		{
			ControllerContext = new() { HttpContext = new DefaultHttpContext { User = Principal(Id) } }
		};
		service.Failure = new ReadDatabaseException();
		var unavailable = Assert.IsType<ObjectResult>(await controller.GetAccountAsync(Guid.Parse(Id), default));
		Assert.Equal(503, unavailable.StatusCode);
		Assert.DoesNotContain("synthetic private diagnostic", JsonSerializer.Serialize(unavailable.Value));
		service.Failure = new ArgumentException("programming error");
		await Assert.ThrowsAsync<ArgumentException>(() => controller.GetAccountAsync(Guid.Parse(Id), default));
		service.Failure = null;
		using var source = new CancellationTokenSource();
		source.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.GetAccountAsync(Guid.Parse(Id), source.Token));
	}

	private sealed class ReadDatabaseException() : DbException("synthetic private diagnostic");
	private sealed class FixedUser : ICurrentUser { public Guid? UserId => Guid.Parse(Id); }
	private static ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity([new Claim("sub", id)], "Test"));

	private sealed class Host(WebApplication app, HttpClient client, ReadStub service) : IAsyncDisposable
	{
		public HttpClient Client { get; } = client;
		public ReadStub Service { get; } = service;
		public void Authenticate(string id) => Client.DefaultRequestHeaders.Add("X-Test-Identity", id);
		public static async Task<Host> StartAsync()
		{
			var builder = WebApplication.CreateBuilder();
			builder.WebHost.UseTestServer();
			builder.Logging.ClearProviders();
			builder.Services.AddControllers().AddApplicationPart(typeof(BankingReadController).Assembly);
			builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddScoped<ICurrentUser, CurrentUser>();
			var service = new ReadStub();
			builder.Services.AddSingleton<IBankingReadService>(service);
			var app = builder.Build();
			app.UseAuthentication();
			app.UseAuthorization();
			app.MapControllers();
			await app.StartAsync();
			return new(app, app.GetTestClient(), service);
		}
		public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
	}

	// Only this test host accepts an identity header; production uses the User JWT handler.
	private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
		: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
			Request.Headers.TryGetValue("X-Test-Identity", out var id)
				? AuthenticateResult.Success(new AuthenticationTicket(Principal(id.ToString()), Scheme.Name)) : AuthenticateResult.NoResult());
	}
	private sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
	{
		public Guid? UserId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("sub"), out var id) ? id : null;
	}

	private sealed class ReadStub : IBankingReadService
	{
		public int Calls { get; private set; }
		public Guid Owner { get; private set; }
		public BankingErrorCode? Error { get; set; }
		public Exception? Failure { get; set; }
		public decimal Amount { get; set; } = -123.45m;
		public BankAccountReadQuery? AccountQuery { get; private set; }
		public BankTransactionReadQuery? TransactionQuery { get; private set; }
		private static BankAccountDetails Account => new(Guid.Parse(Id), "Synthetic", null, null, null, null, "PLN", true,
			new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
		private BankTransactionDetails Transaction => new(Guid.Parse(Id), Guid.Parse(Id), Amount, "PLN", new(2026, 1, 10), null, "Booked");
		private Task<BankingResult<T>> Result<T>(Guid userId, T value, CancellationToken ct) where T : notnull
		{
			ct.ThrowIfCancellationRequested();
			if (Failure is not null) throw Failure;
			Calls++; Owner = userId;
			return Task.FromResult(Error.HasValue ? BankingResult<T>.Failure(Error.Value) : BankingResult<T>.Success(value));
		}
		public Task<BankingResult<PagedResult<BankAccountDetails>>> GetAccountsAsync(Guid userId, BankAccountReadQuery query, CancellationToken cancellationToken = default)
		{
			AccountQuery = query;
			return Result(userId, new PagedResult<BankAccountDetails>([Account], 1, query.PageNumber, query.PageSize), cancellationToken);
		}
		public Task<BankingResult<BankAccountDetails>> GetAccountAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default) => Result(userId, Account, cancellationToken);
		public Task<BankingResult<PagedResult<BankTransactionDetails>>> GetTransactionsAsync(Guid userId, BankTransactionReadQuery query, CancellationToken cancellationToken = default)
		{
			TransactionQuery = query;
			return Result(userId, new PagedResult<BankTransactionDetails>([Transaction], 1, query.PageNumber, query.PageSize), cancellationToken);
		}
		public Task<BankingResult<BankTransactionDetails>> GetTransactionAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken = default) => Result(userId, Transaction, cancellationToken);
	}
}
