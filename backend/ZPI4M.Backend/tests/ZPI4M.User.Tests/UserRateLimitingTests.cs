using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace ZPI4M.User.Tests;

public sealed partial class UserControllerTests
{
	[Theory]
	[InlineData("login", 30, 200)]
	[InlineData("refresh", 120, 200)]
	[InlineData("reset-password", 10, 204)]
	public async Task DefaultLimitsAllowExactlyTheConfiguredNumberOfRequests(string route, int limit, int status)
	{
		await using var host = await TestHost.StartAsync();
		for (var i = 0; i < limit; i++)
		{
			using var response = await host.Client.PostAsJsonAsync("/api/users/" + route, ValidBody());
			Assert.Equal(status, (int)response.StatusCode);
		}
		using var rejected = await host.Client.PostAsJsonAsync("/api/users/" + route, ValidBody());
		Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
		Assert.Equal(limit, host.Services.Calls);
	}

	[Theory]
	[InlineData("login")]
	[InlineData("refresh")]
	[InlineData("reset-password")]
	public async Task ConfiguredLimitsAreIndependentAndPartitionedByIp(string route)
	{
		await using var host = await TestHost.StartAsync(SmallLimits());
		for (var i = 0; i < 2; i++)
		{
			using var response = await host.Client.PostAsJsonAsync("/api/users/" + route, ValidBody());
			Assert.True(response.IsSuccessStatusCode);
		}
		using var rejected = await host.Client.PostAsJsonAsync("/api/users/" + route, ValidBody());
		Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
		Assert.Equal(2, host.Services.Calls);

		foreach (var other in new[] { "login", "refresh", "reset-password", "forgot-password" }.Where(other => other != route))
		{
			using var response = await host.Client.PostAsJsonAsync("/api/users/" + other, ValidBody());
			Assert.True(response.IsSuccessStatusCode);
		}

		// Forwarding headers must not allow clients to bypass their IP bucket.
		host.Client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.2");
		using var stillRejected = await host.Client.PostAsJsonAsync("/api/users/" + route, ValidBody());
		Assert.Equal(HttpStatusCode.TooManyRequests, stillRejected.StatusCode);
		host.Client.DefaultRequestHeaders.Add("X-Test-IP", "192.0.2.2");
		using var differentIp = await host.Client.PostAsJsonAsync("/api/users/" + route, ValidBody());
		Assert.True(differentIp.IsSuccessStatusCode);
	}

	[Fact]
	public async Task FailedLoginsConsumeTheLoginLimit()
	{
		await using var host = await TestHost.StartAsync(SmallLimits());
		host.Services.Result = 1;
		for (var i = 0; i < 2; i++)
		{
			using var response = await host.Client.PostAsJsonAsync("/api/users/login", ValidBody());
			Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		}
		using var rejected = await host.Client.PostAsJsonAsync("/api/users/login", ValidBody());
		Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
		Assert.Equal(2, host.Services.Calls);
	}

	[Fact]
	public async Task InvalidBodiesConsumePermitsWithoutCallingServices()
	{
		await using var host = await TestHost.StartAsync(SmallLimits());
		for (var i = 0; i < 2; i++)
		{
			using var response = await host.Client.PostAsJsonAsync("/api/users/login", new { });
			Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		}
		using var rejected = await host.Client.PostAsJsonAsync("/api/users/login", ValidBody());
		Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
		Assert.Equal(0, host.Services.Calls);
	}

	[Fact]
	public async Task EmailLimitRemainsSharedAndDoesNotBlockAuthentication()
	{
		await using var host = await TestHost.StartAsync();
		for (var i = 0; i < 5; i++)
		{
			var route = i % 2 == 0 ? "forgot-password" : "resend-confirmation";
			using var response = await host.Client.PostAsJsonAsync("/api/users/" + route, ValidBody());
			Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		}
		using var rejected = await host.Client.PostAsJsonAsync("/api/users/register", ValidBody());
		Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
		Assert.Equal(5, host.Services.Calls);
		foreach (var route in new[] { "login", "refresh", "reset-password" })
		{
			using var response = await host.Client.PostAsJsonAsync("/api/users/" + route, ValidBody());
			Assert.True(response.IsSuccessStatusCode);
		}
	}

	[Theory]
	[InlineData("LoginPermitLimit", 0)]
	[InlineData("LoginPermitLimit", -1)]
	[InlineData("RefreshPermitLimit", 0)]
	[InlineData("RefreshPermitLimit", -1)]
	[InlineData("PasswordResetPermitLimit", 0)]
	[InlineData("PasswordResetPermitLimit", -1)]
	public async Task InvalidLimitsPreventHostStartup(string setting, int value)
	{
		var configuration = new Dictionary<string, string?> { ["UserRateLimiting:" + setting] = value.ToString() };
		await Assert.ThrowsAsync<OptionsValidationException>(async () =>
		{
			await using var host = await TestHost.StartAsync(configuration);
		});
	}

	private static Dictionary<string, string?> SmallLimits() => new()
	{
		["UserRateLimiting:LoginPermitLimit"] = "2",
		["UserRateLimiting:RefreshPermitLimit"] = "2",
		["UserRateLimiting:PasswordResetPermitLimit"] = "2"
	};
}
