using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Api.Services;
using ZPI4M.Banking.ApiControllers;
using ZPI4M.Banking.Services;
using ZPI4M.Banking.Services.Connections;

namespace ZPI4M.Banking.IntegrationTests;

public sealed partial class BankingHttpTests : PostgresFixture
{
	[PostgresFact]
	public async Task HttpFlowUsesRealPersistenceAndAnonymousCallback()
	{
		var userId = await AddUserAsync();
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Services.AddControllers().AddApplicationPart(typeof(BankingController).Assembly)
		 .AddApplicationPart(typeof(HttpBankingCurrentUser).Assembly);
		builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
		builder.Services.AddAuthorization();
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddScoped<IBankingCurrentUser, HttpBankingCurrentUser>();
		builder.Services.AddScoped(_ => OpenSession());
		builder.Services.AddScoped<IBankingConnectionService>(provider => provider.GetRequiredService<Session>().Service);
		builder.Services.AddScoped<IBankingCallbackService>(provider => provider.GetRequiredService<Session>().Service);
		await using var app = builder.Build();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers();
		await app.StartAsync();
		using var client = app.GetTestClient();
		using (var denied = await client.PostAsync("/api/banking/connection-attempts", null))
			Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
		using (var removed = await client.PostAsync("/api/integrations/tink/link", null))
			Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
		client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
		using var started = await client.PostAsync("/api/banking/connection-attempts", null);
		Assert.Equal(HttpStatusCode.Created, started.StatusCode);
		Assert.True(started.Headers.CacheControl?.NoStore);
		using var json = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
		var link = new Uri(json.RootElement.GetProperty("authorizationUrl").GetString()!);
		using (var pending = await client.GetAsync(started.Headers.Location))
			Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
		client.DefaultRequestHeaders.Remove("X-Test-User");
		var callbackUrl = "/api/banking/tink/callback" + link.Query + "&credentials_id=demo-credential";
		using var completed = await client.GetAsync(callbackUrl);
		Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
		Assert.True(completed.Headers.CacheControl?.NoStore);
		using var completedJson = JsonDocument.Parse(await completed.Content.ReadAsStringAsync());
		Assert.Equal("COMPLETED", completedJson.RootElement.GetProperty("status").GetString());
		using (var replay = await client.GetAsync(callbackUrl))
			Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
		using (var invalid = await client.GetAsync("/api/banking/tink/callback?state=invalid&credentials_id=demo"))
			Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
		client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
		using var connections = await client.GetAsync("/api/banking/connections");
		using var list = JsonDocument.Parse(await connections.Content.ReadAsStringAsync());
		Assert.Equal(1, list.RootElement.GetArrayLength());
		Assert.Equal("NOT_STARTED", list.RootElement[0].GetProperty("syncStatus").GetString());
		Tink.FailLink = true;
		using var failed = await client.PostAsync("/api/banking/connection-attempts", null);
		Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
		using var problem = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
		Assert.Equal("BANKING_TEMPORARILY_UNAVAILABLE", problem.RootElement.GetProperty("code").GetString());
		Assert.NotEqual(Guid.Empty, problem.RootElement.GetProperty("attemptId").GetGuid());
	}

	// Test-only scheme: the production host contains no identity bypass.
	private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
	 ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue("X-Test-User", out var id))
				return Task.FromResult(AuthenticateResult.NoResult());
			var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name));
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
		}
	}
}
