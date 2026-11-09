using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using ZPI4M.Banking.ApiControllers;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories.BankRefreshRepository;
using ZPI4M.Banking.Services;
using ZPI4M.Banking.Services.Refresh;
using ZPI4M.Banking.Tink;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.User.Database;
using ZPI4M.User.Services.BankingProfile;

namespace ZPI4M.Banking.IntegrationTests;

public sealed partial class BankingHttpTests
{
	[PostgresFact]
	public async Task RefreshHttpContractAndWorkerUseDurableJobsAndOwnerIsolation()
	{
		var owner = await AddUserAsync(tinkUserId: "synthetic-owner");
		var noMapping = await AddUserAsync();
		var inactive = await AddUserAsync(active: false, tinkUserId: "inactive-owner");
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Services.AddControllers().AddApplicationPart(typeof(BankingRefreshController).Assembly);
		builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
		builder.Services.AddAuthorization();
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
		builder.Services.AddDbContext<BankingDbContext>(options => options.UseNpgsql(ConnectionString));
		builder.Services.AddDbContext<UserDbContext>(options => options.UseNpgsql(ConnectionString));
		builder.Services.AddSingleton<TimeProvider>(Clock);
		builder.Services.AddOptions<BankRefreshOptions>();
		builder.Services.AddScoped<BankRefreshRepository>();
		builder.Services.AddScoped<BankRefreshService>();
		builder.Services.AddScoped<BankRefreshProcessor>();
		builder.Services.AddScoped<IUserBankingProfileService, UserBankingProfileService>();
		builder.Services.AddSingleton<ITinkDataGateway, EmptyData>();
		builder.Services.AddSingleton<BankRefreshWorker>();
		await using var app = builder.Build();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers();
		await app.StartAsync();
		using var client = app.GetTestClient();
		using (var denied = await client.PostAsync("/api/banking/refreshes", null)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
		client.DefaultRequestHeaders.Add("X-Test-User", inactive.ToString());
		using (var forbidden = await client.PostAsync("/api/banking/refreshes", null)) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
		client.DefaultRequestHeaders.Remove("X-Test-User");
		client.DefaultRequestHeaders.Add("X-Test-User", noMapping.ToString());
		using (var missing = await client.PostAsync("/api/banking/refreshes", null)) Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
		client.DefaultRequestHeaders.Remove("X-Test-User");
		client.DefaultRequestHeaders.Add("X-Test-User", owner.ToString());
		using var start = await client.PostAsync("/api/banking/refreshes", null);
		Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
		Assert.True(start.Headers.CacheControl?.NoStore);
		using var startedJson = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
		var id = startedJson.RootElement.GetProperty("refreshId").GetGuid();
		using (var duplicate = await client.PostAsync("/api/banking/refreshes", null))
		{
			using var json = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
			Assert.Equal(id, json.RootElement.GetProperty("refreshId").GetGuid());
		}
		var worker = app.Services.GetRequiredService<BankRefreshWorker>();
		await worker.StartAsync(default);
		try
		{
			using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			while (true)
			{
				using var status = await client.GetAsync(start.Headers.Location, deadline.Token);
				Assert.True(status.Headers.CacheControl?.NoStore);
				using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync(deadline.Token));
				Assert.False(json.RootElement.TryGetProperty("tinkUserId", out _));
				Assert.False(json.RootElement.TryGetProperty("leaseId", out _));
				if (json.RootElement.GetProperty("status").GetString() == "SUCCEEDED") break;
				await Task.Delay(25, deadline.Token);
			}
		}
		finally { await worker.StopAsync(default); }
		client.DefaultRequestHeaders.Remove("X-Test-User");
		client.DefaultRequestHeaders.Add("X-Test-User", noMapping.ToString());
		using var foreign = await client.GetAsync(start.Headers.Location);
		Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
		using var absent = await client.GetAsync($"/api/banking/refreshes/{Guid.NewGuid()}");
		Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
	}

	private sealed class EmptyData : ITinkDataGateway, ITinkDataSession
	{
		public Task<ITinkDataSession> OpenAsync(string userId, CancellationToken ct) => Task.FromResult<ITinkDataSession>(this);
		public Task<IReadOnlyList<TinkBankAccount>> GetAccountsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<TinkBankAccount>>([]);
		public Task<IReadOnlyList<TinkBankTransaction>> GetTransactionsAsync(string accountId, DateTimeOffset from, DateTimeOffset through, CancellationToken ct) => throw new InvalidOperationException();
		public void Dispose() { }
	}
}
