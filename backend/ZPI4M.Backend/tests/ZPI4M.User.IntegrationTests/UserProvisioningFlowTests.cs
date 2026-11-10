using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZPI4M.Banking.ApiControllers;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories.BankingConnectionRepository;
using ZPI4M.Banking.Services.Connections;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Tink;
using ZPI4M.Banking.Tink.Http;
using ZPI4M.User.Services.BankingProfile;

namespace ZPI4M.User.IntegrationTests;

public sealed class UserProvisioningFlowTests : PostgresFixture
{
	[PostgresTheory]
	[InlineData("upstream_error")]
	[InlineData("network_error")]
	[InlineData("timeout")]
	[InlineData("invalid_response")]
	public async Task FirstConnectionInitializesTinkAndCanBeRetriedAfterOutage(string errorCode)
	{
		var tink = new ProvisioningTink { ErrorCode = errorCode, Fail = true };
		await using var host = await StartHostAsync(configureServices: services =>
		{
			services.AddControllers().AddApplicationPart(typeof(BankingController).Assembly);
			services.AddScoped<IUserBankingProfileService, UserBankingProfileService>();
			services.AddSingleton<ITinkConnectionGateway>(tink);
			services.AddSingleton<TimeProvider>(TimeProvider.System);
			services.AddDbContextFactory<BankingDbContext>(options => options.UseNpgsql(ConnectionString), ServiceLifetime.Scoped);
			services.AddScoped<BankingConnectionRepository>();
			services.AddScoped<IBankConnectionRepository>(p => p.GetRequiredService<BankingConnectionRepository>());
			services.AddScoped<IBankConnectionAttemptRepository>(p => p.GetRequiredService<BankingConnectionRepository>());
			services.AddOptions<BankingConnectionOptions>();
			services.AddScoped<IBankingConnectionService, BankingConnectionService>();
		});
		using (var anonymous = await host.Client.PostAsync("/api/banking/connection-attempts", null))
			Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
		using var registered = await host.Client.PostAsJsonAsync("/api/users/register", new
		{
			login = "provision.user",
			name = "Test",
			surname = "User",
			email = "provision@example.test",
			password = Password,
			birthDate = "2000-01-01"
		});
		Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
		var userId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
		using (var confirmed = await host.Client.PostAsJsonAsync("/api/users/confirm-email", new { token = Email.LastToken }))
			Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
		using var login = await host.Client.PostAsJsonAsync("/api/users/login", new { login = "provision.user", password = Password });
		Assert.Equal(HttpStatusCode.OK, login.StatusCode);
		var accessToken = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
		host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using (var removed = await host.Client.PostAsync("/api/banking/tink-user", null))
			Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
		Assert.Equal(0, tink.CreateCalls);
		await using (var database = OpenDatabase())
			Assert.Null((await database.Users.SingleAsync(x => x.Id == userId)).TinkUserId);

		// A Tink outage cannot undo registration/login. Retrying uses the same session.
		tink.Fail = true;
		Guid failedAttemptId;
		using (var failed = await host.Client.PostAsync("/api/banking/connection-attempts", null))
		{
			Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
			var problem = await failed.Content.ReadFromJsonAsync<JsonElement>();
			Assert.Equal("BANKING_TEMPORARILY_UNAVAILABLE", problem.GetProperty("code").GetString());
			failedAttemptId = problem.GetProperty("attemptId").GetGuid();
			Assert.DoesNotContain("private diagnostic", await failed.Content.ReadAsStringAsync());
		}
		await using (var database = OpenDatabase())
			Assert.Null((await database.Users.SingleAsync(x => x.Id == userId)).TinkUserId);
		await using (var database = new BankingDbContext(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(ConnectionString).Options))
		{
			var attempt = await database.ConnectionAttempts.SingleAsync(x => x.Id == failedAttemptId);
			Assert.Equal(BankConnectionAttemptStatus.Failed, attempt.Status);
			Assert.Equal("TINK_UNAVAILABLE", attempt.FailureCode);
			Assert.Null(attempt.TinkUserId);
			Assert.Empty(await database.Connections.ToListAsync());
			Assert.Empty(await database.Refreshes.ToListAsync());
		}
		tink.Fail = false;
		for (var i = 0; i < 2; i++)
		{
			using var connected = await host.Client.PostAsync("/api/banking/connection-attempts", null);
			Assert.Equal(HttpStatusCode.Created, connected.StatusCode);
			var result = await connected.Content.ReadFromJsonAsync<JsonElement>();
			Assert.NotEqual(failedAttemptId, result.GetProperty("attemptId").GetGuid());
		}
		await using (var database = OpenDatabase())
			Assert.Equal($"tink-{userId:N}", (await database.Users.SingleAsync(x => x.Id == userId)).TinkUserId);
		Assert.Equal($"tink-{userId:N}", tink.LinkUser);
		Assert.Equal(2, tink.CreateCalls); // One failed call and one successful call; subsequent start reuses the mapping.
	}

	private sealed class ProvisioningTink : ITinkConnectionGateway
	{
		public bool Fail { get; set; }
		public required string ErrorCode { get; init; }
		public int CreateCalls { get; private set; }
		public string? LinkUser { get; private set; }
		public Task<string> CreateOrRecoverUserAsync(Guid userId, CancellationToken cancellationToken)
		{
			CreateCalls++;
			return Fail ? throw new TinkApiException("private diagnostic", ErrorCode) : Task.FromResult($"tink-{userId:N}");
		}
		public Task<string> CreateLinkAsync(string tinkUserId, string idHint, string state, CancellationToken cancellationToken)
		{
			LinkUser = tinkUserId;
			return Task.FromResult("https://example.test/link");
		}
		public Task<TinkVerifiedConnection?> VerifyConnectionAsync(string tinkUserId, string credentialsId, CancellationToken cancellationToken) => throw new InvalidOperationException();
		public Task<IReadOnlyList<TinkProviderConsent>> GetConsentsAsync(string tinkUserId, CancellationToken cancellationToken) => throw new InvalidOperationException();
	}
}
