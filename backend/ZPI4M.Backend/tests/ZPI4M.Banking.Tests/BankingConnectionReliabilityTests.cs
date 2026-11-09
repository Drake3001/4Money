using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ZPI4M.Banking.Tests;

public sealed partial class BankingControllerTests
{
	public static IEnumerable<object[]> DatabaseFailureCases()
	{
		foreach (var (method, path) in new[]
		{
			("POST", "/api/banking/connection-attempts"),
			("GET", "/api/banking/connection-attempts/00000000-0000-0000-0000-000000000001"),
			("GET", "/api/banking/connections"),
			("GET", "/api/banking/connections/00000000-0000-0000-0000-000000000001"),
			("GET", "/api/banking/consents"),
			("GET", "/api/banking/tink/callback?state=synthetic-state&credentials_id=synthetic-credential")
		})
			foreach (var update in new[] { false, true })
				yield return new object[] { method, path, update };
	}

	[Theory]
	[MemberData(nameof(DatabaseFailureCases))]
	public async Task DatabaseFailuresReturnSafeUncacheableProblemDetails(string method, string path, bool update)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate(host.Service.OwnerId);
		host.Service.Failure = update ? new DbUpdateException("private synthetic diagnostic") : new ConnectionDatabaseException();
		using var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		Assert.True(response.Headers.CacheControl?.NoStore);
		var body = await response.Content.ReadAsStringAsync();
		Assert.DoesNotContain("private synthetic diagnostic", body);
		Assert.DoesNotContain("synthetic-state", body);
		Assert.DoesNotContain("synthetic-credential", body);
		var problem = await ReadJson(response);
		Assert.Equal(503, problem.GetProperty("status").GetInt32());
		Assert.Equal("BANKING_TEMPORARILY_UNAVAILABLE", problem.GetProperty("code").GetString());
		Assert.Equal(System.Text.Json.JsonValueKind.Null, problem.GetProperty("attemptId").ValueKind);
		if (path.Contains("callback"))
		{
			Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);
			Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
		}
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task OtherExceptionsAreNotReclassified(bool callback, bool cancelled)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate(host.Service.OwnerId);
		host.Service.Failure = cancelled ? new OperationCanceledException() : new InvalidOperationException("programming error");
		var path = callback ? "/api/banking/tink/callback?state=test&credentials_id=test" : "/api/banking/connections";
		if (cancelled)
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Client.GetAsync(path));
		else
			await Assert.ThrowsAsync<InvalidOperationException>(() => host.Client.GetAsync(path));
	}

	[Fact]
	public async Task ConnectionReadsAndBusinessErrorsAreUncacheable()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate(host.Service.OwnerId);
		foreach (var path in new[]
		{
			$"/api/banking/connection-attempts/{host.Service.AttemptId}",
			"/api/banking/connections",
			$"/api/banking/connections/{host.Service.ConnectionId}",
			$"/api/banking/connection-attempts/{Guid.NewGuid()}",
			$"/api/banking/connections/{Guid.NewGuid()}"
		})
		{
			using var response = await host.Client.GetAsync(path);
			Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound);
			Assert.True(response.Headers.CacheControl?.NoStore);
		}
	}

	private sealed class ConnectionDatabaseException() : DbException("private synthetic diagnostic");
}
