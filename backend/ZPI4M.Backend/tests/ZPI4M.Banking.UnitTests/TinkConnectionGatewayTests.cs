using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Banking.Tink;
using ZPI4M.Banking.Tink.Authentication;
using ZPI4M.Banking.Tink.Http;
using ZPI4M.Banking.Tink.Link;

namespace ZPI4M.Banking.UnitTests;

public sealed partial class TinkTests
{
	[Fact]
	public async Task UnverifiedProductNeverCallsTink()
	{
		using var fixture = new Fixture();
		var gateway = Gateway(fixture);
		var error = await Assert.ThrowsAsync<TinkApiException>(() => gateway.CreateOrRecoverUserAsync(Guid.NewGuid(), default));
		Assert.Equal("continuous_access_unverified", error.ErrorCode);
	}

	[Fact]
	public async Task CreatesUserWithStableExternalIdAndMinimumScope()
	{
		using var fixture = new Fixture();
		fixture.Settings.ContinuousAccessVerified = true;
		var userId = Guid.NewGuid();
		var externalId = $"4money-{userId:N}";
		var calls = 0;
		fixture.Handler.Respond = async (request, ct) =>
		{
			calls++;
			if (request.RequestUri!.AbsolutePath.EndsWith("oauth/token"))
			{
				var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
				Assert.Equal("user:create", form["scope"]);
				return Json("{\"access_token\":\"client\",\"expires_in\":1800}");
			}
			Assert.Equal("/api/v1/user/create", request.RequestUri.AbsolutePath);
			Assert.Equal("client", request.Headers.Authorization!.Parameter);
			using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
			Assert.Equal(externalId, json.RootElement.GetProperty("external_user_id").GetString());
			Assert.Equal("PL", json.RootElement.GetProperty("market").GetString());
			return Json(JsonSerializer.Serialize(new { user_id = "created", external_user_id = externalId }));
		};
		Assert.Equal("created", await Gateway(fixture).CreateOrRecoverUserAsync(userId, default));
		Assert.Equal(2, calls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ExistingRemoteUserIsRecoveredAfterLocalMappingWasLost(bool lostResponse)
	{
		using var fixture = new Fixture();
		fixture.Settings.ContinuousAccessVerified = true;
		var userId = Guid.NewGuid();
		var externalId = $"4money-{userId:N}";
		var createCalls = 0;
		fixture.Handler.Respond = async (request, ct) =>
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path.EndsWith("user/create"))
			{
				createCalls++;
				if (lostResponse)
					throw new TaskCanceledException();
				return new HttpResponseMessage(HttpStatusCode.Conflict);
			}
			if (path.EndsWith("authorization-grant"))
			{
				var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
				Assert.Equal(externalId, form["external_user_id"]);
				Assert.False(form.ContainsKey("user_id"));
				Assert.Equal("user:read", form["scope"]);
				return Json("{\"code\":\"fresh-code\"}");
			}
			if (path == "/api/v1/user")
				return Json(JsonSerializer.Serialize(new { id = "recovered", externalUserId = externalId }));
			return Json("{\"access_token\":\"token\",\"expires_in\":1800}");
		};
		Assert.Equal("recovered", await Gateway(fixture).CreateOrRecoverUserAsync(userId, default));
		Assert.Equal(1, createCalls);
	}

	[Theory]
	[InlineData("owner", "UPDATED", true)]
	[InlineData("another-user", "UPDATED", false)]
	public async Task CredentialsAreVerifiedUnderTheirOwner(string returnedOwner, string status, bool accepted)
	{
		using var fixture = new Fixture();
		fixture.Settings.ContinuousAccessVerified = true;
		fixture.Handler.Respond = async (request, ct) =>
		{
			var path = request.RequestUri!.AbsolutePath;
			if (path.EndsWith("authorization-grant"))
			{
				var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
				Assert.Equal("owner", form["user_id"]);
				Assert.Equal("credentials:read", form["scope"]);
				return Json("{\"code\":\"fresh-code\"}");
			}
			if (path.EndsWith("credentials/credential"))
			{
				Assert.Equal("token", request.Headers.Authorization!.Parameter);
				return Json(JsonSerializer.Serialize(new { id = "credential", userId = returnedOwner, providerName = "demo", status }));
			}
			return Json("{\"access_token\":\"token\",\"expires_in\":1800}");
		};
		Assert.Equal(accepted, await Gateway(fixture).VerifyConnectionAsync("owner", "credential", default) is not null);
	}

	[Fact]
	public void CorrelationIsEncodedWithoutChangingRedirectUri()
	{
		using var fixture = new Fixture();
		var query = QueryHelpers.ParseQuery(new Uri(new TinkLinkUrlBuilder(Options.Create(fixture.Settings)).Build("code", "state+/=")).Query);
		Assert.Equal("state+/=", query["state"]);
		Assert.Equal(fixture.Settings.RedirectUri, query["redirect_uri"]);
	}


	[Fact]
	public async Task NotReadyCredentialsAreRetryableInsteadOfConnected()
	{
		using var fixture = new Fixture();
		fixture.Settings.ContinuousAccessVerified = true;
		fixture.Handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
		{
			"/api/v1/oauth/authorization-grant" => Json("{\"code\":\"code\"}"),
			"/api/v1/credentials/credential" => Json("{\"id\":\"credential\",\"userId\":\"owner\",\"providerName\":\"demo\",\"status\":\"UPDATING\"}"),
			_ => Json("{\"access_token\":\"token\",\"expires_in\":1800}")
		});
		var error = await Assert.ThrowsAsync<TinkApiException>(() => Gateway(fixture).VerifyConnectionAsync("owner", "credential", default));
		Assert.Equal("connection_not_ready", error.ErrorCode);
	}

	[Fact]
	public async Task ClientTokensAreCachedSeparatelyForEachScopeSet()
	{
		using var fixture = new Fixture();
		var requests = 0;
		fixture.Handler.Respond = async (request, ct) =>
		{
			requests++;
			var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
			return Json(JsonSerializer.Serialize(new { access_token = form["scope"].ToString(), expires_in = 1800 }));
		};
		Assert.Equal("authorization:grant", await fixture.Provider.GetTokenAsync());
		Assert.Equal("user:create", await fixture.Provider.GetTokenAsync(["user:create"]));
		Assert.Equal("authorization:grant", await fixture.Provider.GetTokenAsync());
		Assert.Equal(2, requests);
	}

	[Fact]
	public void ResolvingGatewayDoesNotRequireTinkConfigurationForLocalReads()
	{
		var config = new ConfigurationBuilder().Build();
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddTink(config);
		using var provider = services.BuildServiceProvider();
		Assert.NotNull(provider.GetRequiredService<ITinkConnectionGateway>());
	}

	private static TinkConnectionGateway Gateway(Fixture fixture) => new(fixture.Provider,
	 new TinkAuthorizationService(fixture.Provider, fixture.Handler, Options.Create(fixture.Settings)),
	 new TinkLinkUrlBuilder(Options.Create(fixture.Settings)), fixture.Handler, Options.Create(fixture.Settings));
}
