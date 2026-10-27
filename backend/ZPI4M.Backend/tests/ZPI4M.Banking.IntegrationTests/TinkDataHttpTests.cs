using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Banking.Services.Refresh;
using ZPI4M.Banking.Tink;
using ZPI4M.Banking.Tink.Authentication;
using ZPI4M.Banking.Tink.Configuration;
using ZPI4M.Banking.Tink.Http;

namespace ZPI4M.Banking.IntegrationTests;

// Real HTTP over a loopback socket exercises the production adapter and token flow.
public sealed class TinkDataHttpTests
{
	[Theory]
	[InlineData("success")]
	[InlineData("forbidden")]
	[InlineData("rate_limit")]
	[InlineData("loop")]
	[InlineData("wrong_account")]
	[InlineData("timeout")]
	[InlineData("html_error")]
	[InlineData("empty")]
	[InlineData("pending")]
	[InlineData("accounts_loop")]
	[InlineData("accounts_invalid")]
	public async Task AdapterUsesScopedTokenPaginationFiltersAndSafeFailures(string scenario)
	{
		var requests = new ConcurrentQueue<string>();
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Logging.ClearProviders();
		await using var app = builder.Build();
		app.MapPost("/api/v1/oauth/token", async (HttpRequest request) =>
		{
			requests.Enqueue(request.Path);
			var form = await request.ReadFormAsync();
			if (form["grant_type"] == "client_credentials")
			{
				Assert.Equal("authorization:grant", form["scope"]);
				return Results.Json(new { access_token = "synthetic-client", expires_in = 1800 });
			}
			Assert.Equal("synthetic-code", form["code"]);
			return Results.Json(new { access_token = "synthetic-user", expires_in = 1800 });
		});
		app.MapPost("/api/v1/oauth/authorization-grant", async (HttpRequest request) =>
		{
			requests.Enqueue(request.Path);
			Assert.Equal("Bearer synthetic-client", request.Headers.Authorization);
			var form = await request.ReadFormAsync();
			Assert.Equal("synthetic-owner", form["user_id"]);
			Assert.Equal("accounts:read,transactions:read", form["scope"]);
			return Results.Json(new { code = "synthetic-code" });
		});
		app.MapGet("/data/v2/accounts", (HttpRequest request) =>
		{
			requests.Enqueue(request.Path + request.QueryString);
			Assert.Equal("Bearer synthetic-user", request.Headers.Authorization);
			if (scenario == "accounts_invalid") return Results.Json(new { unexpected = true });
			var second = request.Query.ContainsKey("pageToken");
			return Results.Json(new { accounts = new[] { new { id = second ? "synthetic-second" : "synthetic-account", name = "Test", balances = new { booked = new { amount = new { currencyCode = "PLN" } } } } }, nextPageToken = !second || scenario == "accounts_loop" ? "accounts-next" : "" });
		});
		app.MapGet("/data/v2/transactions", async (HttpContext context) =>
		{
			var request = context.Request;
			requests.Enqueue(request.Path + request.QueryString);
			Assert.Equal("Bearer synthetic-user", request.Headers.Authorization);
			Assert.Equal("synthetic-account", request.Query["accountIdIn"]);
			Assert.Equal("BOOKED", request.Query["statusIn"]);
			Assert.Equal("2026-10-01", request.Query["bookedDateGte"]);
			Assert.Equal("2026-10-25", request.Query["bookedDateLte"]);
			Assert.Equal("100", request.Query["pageSize"]);
			if (scenario == "timeout")
			{
				await Task.Delay(TimeSpan.FromSeconds(2), context.RequestAborted);
				return Results.Json(new { transactions = Array.Empty<object>() });
			}
			if (scenario == "empty") return Results.Json(new { transactions = Array.Empty<object>(), nextPageToken = "" });
			if (scenario == "forbidden") return Results.StatusCode(403);
			if (scenario == "html_error") return Results.Content("<html>upstream</html>", "text/html", statusCode: 502);
			if (scenario == "rate_limit")
			{
				context.Response.Headers.RetryAfter = "120";
				return Results.StatusCode(429);
			}
			var second = request.Query.ContainsKey("pageToken");
			if (second) Assert.Equal("next +/&", request.Query["pageToken"]);
			var id = second ? "tx-2" : "tx-1";
			return Results.Json(new
			{
				transactions = new[] { new { id, accountId = scenario == "wrong_account" ? "foreign" : "synthetic-account", status = scenario == "pending" ? "PENDING" : "BOOKED",
					amount = new { currencyCode = "PLN", value = new { unscaledValue = "77", scale = "-3" } }, dates = new { booked = "2026-10-24" }, descriptions = new { original = "Synthetic" } } },
				nextPageToken = !second || scenario == "loop" ? "next +/&" : ""
			});
		});
		await app.StartAsync();
		var factory = new ClientFactory(new Uri(app.Urls.Single()), scenario == "timeout");
		using var cache = new MemoryCache(new MemoryCacheOptions());
		var settings = Options.Create(new TinkOptions { ContinuousAccessVerified = true, ClientId = "synthetic", ClientSecret = "synthetic" });
		using var tokens = new TinkClientTokenProvider(cache, factory, settings, TimeProvider.System);
		var gateway = new TinkDataGateway(new TinkAuthorizationService(tokens, factory, settings), factory, settings, Options.Create(new BankRefreshOptions()));
		using var session = await gateway.OpenAsync("synthetic-owner", default);
		if (scenario.StartsWith("accounts_"))
		{
			await Assert.ThrowsAsync<TinkApiException>(() => session.GetAccountsAsync(default));
			return;
		}
		Assert.Equal(2, (await session.GetAccountsAsync(default)).Count);
		var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
		if (scenario == "empty") Assert.Empty(await session.GetTransactionsAsync("synthetic-account", from, from.AddDays(24), default));
		else if (scenario == "success")
		{
			var data = await session.GetTransactionsAsync("synthetic-account", from, from.AddDays(24), default);
			Assert.Equal(2, data.Count);
			Assert.All(data, item => Assert.Equal(77000m, item.Amount));
		}
		else
		{
			var error = await Assert.ThrowsAsync<TinkApiException>(() => session.GetTransactionsAsync("synthetic-account", from, from.AddDays(24), default));
			Assert.DoesNotContain("synthetic-user", error.Message);
			if (scenario == "rate_limit") Assert.Equal(TimeSpan.FromSeconds(120), error.RetryAfter);
			if (scenario == "forbidden") Assert.Equal(HttpStatusCode.Forbidden, error.UpstreamStatus);
			if (scenario == "timeout") Assert.Equal("timeout", error.ErrorCode);
			if (scenario is "loop" or "wrong_account" or "pending") Assert.Equal("invalid_response", error.ErrorCode);
		}
		Assert.Contains(requests, path => path.StartsWith("/data/v2/transactions"));
		Assert.Contains("/api/v1/oauth/authorization-grant", requests);
	}

	private sealed class ClientFactory(Uri address, bool shortTimeout) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new() { BaseAddress = address, Timeout = shortTimeout ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(5) };
	}
}
