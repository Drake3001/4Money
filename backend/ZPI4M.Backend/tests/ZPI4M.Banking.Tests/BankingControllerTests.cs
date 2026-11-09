using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Banking.ApiControllers;
using ZPI4M.Banking.Services;
using ZPI4M.Banking.Services.Connections;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Shared.CurrentUser;

namespace ZPI4M.Banking.Tests;

public sealed partial class BankingControllerTests
{
	[Theory]
	[InlineData("POST", "/api/banking/connection-attempts")]
	[InlineData("GET", "/api/banking/connection-attempts/00000000-0000-0000-0000-000000000001")]
	[InlineData("GET", "/api/banking/connections")]
	[InlineData("GET", "/api/banking/consents")]
	[InlineData("GET", "/api/banking/connections/00000000-0000-0000-0000-000000000001")]
	public async Task Anonymous_requests_are_rejected_before_calling_service(string method, string path)
	{
		await using var host = await TestHost.StartAsync();
		using var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.Equal(0, host.Service.Calls);
	}

	[Fact]
	public async Task Start_returns_uncacheable_authorization_url_and_resolvable_location()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate(host.Service.OwnerId);
		using var response = await host.Client.PostAsync("/api/banking/connection-attempts", null);
		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
		Assert.Equal($"/api/banking/connection-attempts/{host.Service.AttemptId}",
			response.Headers.Location?.AbsolutePath);
		var json = await ReadJson(response);
		Assert.Equal(host.Service.AttemptId, json.GetProperty("attemptId").GetGuid());
		Assert.Equal("https://example.test/authorize", json.GetProperty("authorizationUrl").GetString());
		using var attempt = await host.Client.GetAsync(response.Headers.Location);
		Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
	}

	[Theory]
	[InlineData(BankConnectionAttemptStatus.Expired, "EXPIRED")]
	[InlineData(BankConnectionAttemptStatus.Failed, "FAILED")]
	[InlineData(BankConnectionAttemptStatus.Cancelled, "CANCELLED")]
	public async Task Terminal_attempts_remain_readable(BankConnectionAttemptStatus status, string expected)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate(host.Service.OwnerId);
		host.Service.AttemptStatus = status;
		using var response = await host.Client.GetAsync($"/api/banking/connection-attempts/{host.Service.AttemptId}");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(expected, (await ReadJson(response)).GetProperty("status").GetString());
	}

	[Fact]
	public async Task Connection_and_sync_states_are_independent_and_provider_ids_are_not_exposed()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate(host.Service.OwnerId);
		using var response = await host.Client.GetAsync($"/api/banking/connections/{host.Service.ConnectionId}");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var json = await ReadJson(response);
		Assert.Equal("CONNECTED", json.GetProperty("connectionStatus").GetString());
		Assert.Equal("FAILED", json.GetProperty("syncStatus").GetString());
		Assert.Equal(JsonValueKind.Null, json.GetProperty("consentExpiresAt").ValueKind);
		Assert.False(json.TryGetProperty("credentialsId", out _));
		Assert.False(json.TryGetProperty("tinkUserId", out _));
	}

	[Fact]
	public async Task Empty_connection_list_is_an_array()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate(Guid.NewGuid());
		using var response = await host.Client.GetAsync("/api/banking/connections");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(0, (await ReadJson(response)).GetArrayLength());
	}

	[Theory]
	[InlineData("connections")]
	[InlineData("connection-attempts")]
	public async Task Client_cannot_override_authenticated_owner(string resource)
	{
		await using var host = await TestHost.StartAsync();
		var otherUser = Guid.NewGuid();
		host.Authenticate(otherUser);
		var id = resource == "connections" ? host.Service.ConnectionId : host.Service.AttemptId;
		using var response = await host.Client.GetAsync($"/api/banking/{resource}/{id}?userId={host.Service.OwnerId}");
		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
		Assert.Equal(otherUser, host.Service.LastUserId);
		Assert.Equal("BANKING_NOT_FOUND", (await ReadJson(response)).GetProperty("code").GetString());
	}

	[Theory]
	[InlineData(BankingErrorCode.NotFound, 404, "BANKING_NOT_FOUND")]
	[InlineData(BankingErrorCode.Conflict, 409, "BANKING_CONFLICT")]
	[InlineData(BankingErrorCode.TemporarilyUnavailable, 503, "BANKING_TEMPORARILY_UNAVAILABLE")]
	public async Task Expected_errors_have_problem_details(BankingErrorCode error, int status, string code)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate(host.Service.OwnerId);
		host.Service.StartError = error;
		using var response = await host.Client.PostAsync("/api/banking/connection-attempts", null);
		Assert.Equal(status, (int)response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		var json = await ReadJson(response);
		Assert.Equal(status, json.GetProperty("status").GetInt32());
		Assert.Equal(code, json.GetProperty("code").GetString());
	}

	[Theory]
	[InlineData("invalid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public async Task Invalid_local_identity_does_not_reach_service(string identity)
	{
		await using var host = await TestHost.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-Identity", identity);
		using var response = await host.Client.GetAsync("/api/banking/connections");
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.Equal(0, host.Service.Calls);
	}

	[Fact]
	public async Task ConsentsUseAuthenticatedOwnerAndExposeOnlyPublicContract()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate(host.Service.OwnerId);
		using var response = await host.Client.GetAsync("/api/banking/consents");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
		var item = (await ReadJson(response))[0];
		Assert.Equal(host.Service.ConnectionId, item.GetProperty("connectionId").GetGuid());
		Assert.Equal("UPDATED", item.GetProperty("providerStatus").GetString());
		Assert.Equal(JsonValueKind.Null, item.GetProperty("sessionExpiresAt").ValueKind);
		Assert.Equal(JsonValueKind.Null, item.GetProperty("sessionExtendable").ValueKind);
		Assert.Equal(new[] { "connectionId", "providerStatus", "sessionExpiresAt", "sessionExtendable" },
		 item.EnumerateObject().Select(property => property.Name).ToArray());
		host.Client.DefaultRequestHeaders.Remove("X-Test-Identity");
		var otherUser = Guid.NewGuid();
		host.Authenticate(otherUser);
		using var other = await host.Client.GetAsync($"/api/banking/consents?userId={host.Service.OwnerId}&tinkUserId=foreign");
		Assert.Equal(HttpStatusCode.OK, other.StatusCode);
		Assert.Empty((await ReadJson(other)).EnumerateArray());
		Assert.Equal(otherUser, host.Service.LastUserId);
	}

	[Theory]
	[InlineData(BankingErrorCode.NotFound, 404, "BANKING_NOT_FOUND")]
	[InlineData(BankingErrorCode.Forbidden, 403, "BANKING_FORBIDDEN")]
	[InlineData(BankingErrorCode.TemporarilyUnavailable, 503, "BANKING_TEMPORARILY_UNAVAILABLE")]
	public async Task ConsentErrorsUseSafeProblemDetails(BankingErrorCode error, int status, string code)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate(host.Service.OwnerId);
		host.Service.ConsentError = error;
		using var response = await host.Client.GetAsync("/api/banking/consents");
		Assert.Equal(status, (int)response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		Assert.Equal(code, (await ReadJson(response)).GetProperty("code").GetString());
	}

	private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
	{
		using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		return document.RootElement.Clone();
	}

	private sealed class TestHost(WebApplication app, HttpClient client, FakeConnectionService service) : IAsyncDisposable
	{
		public HttpClient Client { get; } = client;
		public FakeConnectionService Service { get; } = service;

		public void Authenticate(Guid userId) => Client.DefaultRequestHeaders.Add("X-Test-Identity", userId.ToString());

		public static async Task<TestHost> StartAsync()
		{
			var builder = WebApplication.CreateBuilder();
			builder.WebHost.UseTestServer();
			builder.Logging.ClearProviders();
			builder.Services.AddControllers().AddApplicationPart(typeof(BankingController).Assembly);
			builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddScoped<ICurrentUser, TestCurrentUser>();
			var service = new FakeConnectionService();
			builder.Services.AddSingleton<IBankingConnectionService>(service);
			builder.Services.AddSingleton<IBankingCallbackService>(service);
			var app = builder.Build();
			app.UseAuthentication();
			app.UseAuthorization();
			app.MapControllers();
			await app.StartAsync();
			return new(app, app.GetTestClient(), service);
		}

		public async ValueTask DisposeAsync()
		{
			Client.Dispose();
			await app.DisposeAsync();
		}
	}

	// Test-only identity scheme. Production must use its real authentication handler.
	private sealed class TestAuthenticationHandler(
		IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
		: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue("X-Test-Identity", out var id))
				return Task.FromResult(AuthenticateResult.NoResult());
			var principal = new ClaimsPrincipal(new ClaimsIdentity(
				[new Claim(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name));
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
		}
	}

	private sealed class TestCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
	{
		public Guid? UserId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
			? id : null;
	}

	private sealed class FakeConnectionService : IBankingConnectionService, IBankingCallbackService
	{
		public Guid OwnerId { get; } = Guid.NewGuid();
		public Guid AttemptId { get; } = Guid.NewGuid();
		public Guid ConnectionId { get; } = Guid.NewGuid();
		public Guid LastUserId { get; private set; }
		public int Calls { get; private set; }
		public Exception? Failure { get; set; }
		public BankingErrorCode? StartError { get; set; }
		public BankConnectionAttemptStatus AttemptStatus { get; set; } = BankConnectionAttemptStatus.Pending;

		private void Record(Guid userId, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			LastUserId = userId;
			Calls++;
			if (Failure is not null) throw Failure;
		}

		public Task<BankingResult<StartBankConnectionResult>> StartConnectionAsync(Guid userId, CancellationToken cancellationToken = default)
		{
			Record(userId, cancellationToken);
			return Task.FromResult(StartError is { } error
				? BankingResult<StartBankConnectionResult>.Failure(error)
				: BankingResult<StartBankConnectionResult>.Success(new(AttemptId, "https://example.test/authorize", DateTimeOffset.UtcNow.AddMinutes(10))));
		}

		public Task<BankingResult<BankConnectionAttemptDetails>> GetConnectionAttemptAsync(Guid userId, Guid attemptId, CancellationToken cancellationToken = default)
		{
			Record(userId, cancellationToken);
			return Task.FromResult(userId == OwnerId && attemptId == AttemptId
				? BankingResult<BankConnectionAttemptDetails>.Success(new(AttemptId, AttemptStatus, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), null, null))
				: BankingResult<BankConnectionAttemptDetails>.Failure(BankingErrorCode.NotFound));
		}

		public Task<BankingResult<IReadOnlyList<BankConnectionDetails>>> GetConnectionsAsync(Guid userId, CancellationToken cancellationToken = default)
		{
			Record(userId, cancellationToken);
			return Task.FromResult(BankingResult<IReadOnlyList<BankConnectionDetails>>.Success(userId == OwnerId ? [Connection()] : []));
		}

		public Task<BankingResult<BankConnectionDetails>> GetConnectionAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default)
		{
			Record(userId, cancellationToken);
			return Task.FromResult(userId == OwnerId && connectionId == ConnectionId
				? BankingResult<BankConnectionDetails>.Success(Connection())
				: BankingResult<BankConnectionDetails>.Failure(BankingErrorCode.NotFound));
		}

		public BankingErrorCode? ConsentError { get; set; }
		public Task<BankingResult<IReadOnlyList<BankConsentDetails>>> GetConsentsAsync(Guid userId, CancellationToken cancellationToken = default)
		{
			Record(userId, cancellationToken);
			return Task.FromResult(ConsentError is { } error
			 ? BankingResult<IReadOnlyList<BankConsentDetails>>.Failure(error)
			 : BankingResult<IReadOnlyList<BankConsentDetails>>.Success(userId == OwnerId
			  ? [new(ConnectionId, "UPDATED", null, null)] : []));
		}

		public Task<BankingResult<BankConnectionAttemptDetails>> CompleteAsync(BankingCallback callback, CancellationToken cancellationToken = default)
		{
			Record(OwnerId, cancellationToken);
			return Task.FromResult(BankingResult<BankConnectionAttemptDetails>.Success(
				new(AttemptId, BankConnectionAttemptStatus.Completed, ConnectionId, DateTimeOffset.UtcNow,
					DateTimeOffset.UtcNow.AddMinutes(10), DateTimeOffset.UtcNow, null)));
		}

		private BankConnectionDetails Connection() => new(ConnectionId, "Demo Bank", BankConnectionStatus.Connected,
			BankSynchronizationStatus.Failed, DateTimeOffset.UtcNow, null, null, null, "SYNC_UNAVAILABLE");
	}
}
