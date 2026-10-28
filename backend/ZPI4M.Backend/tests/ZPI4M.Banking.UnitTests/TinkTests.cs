using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Banking.Tink;
using ZPI4M.Banking.Tink.Authentication;
using ZPI4M.Banking.Tink.Configuration;
using ZPI4M.Banking.Tink.Http;
using ZPI4M.Banking.Tink.Link;

namespace ZPI4M.Banking.UnitTests;

public sealed partial class TinkTests
{
	[Fact]
	public async Task FlowSendsOnlyExpectedFormsAndBuildsEncodedUrl()
	{
		using var fixture = new Fixture();
		fixture.Settings.ClientId = "our-client";
		fixture.Settings.ClientSecret = "private-client-secret";
		fixture.Settings.RedirectUri = "https://app.example/callback?next=/konto&label=Łódź";
		const string code = "code +/&?=ż";
		var calls = new List<string>();
		fixture.Handler.Respond = async (request, cancellationToken) =>
		{
			calls.Add(request.RequestUri!.AbsolutePath);
			Assert.Equal(HttpMethod.Post, request.Method);
			Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
			var form = QueryHelpers.ParseQuery(await request.Content.ReadAsStringAsync(cancellationToken));
			if (calls.Count == 1)
			{
				Assert.Null(request.Headers.Authorization);
				Assert.Equal(4, form.Count);
				Assert.Equal("our-client", form["client_id"]);
				Assert.Equal("private-client-secret", form["client_secret"]);
				Assert.Equal("client_credentials", form["grant_type"]);
				Assert.Equal("authorization:grant", form["scope"]);
				return Json("{\"access_token\":\"client-token\",\"expires_in\":1800}");
			}
			Assert.Equal(4, form.Count);
			Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
			Assert.Equal("client-token", request.Headers.Authorization.Parameter);
			Assert.Equal("existing-tink-user", form["user_id"]);
			Assert.Equal(fixture.Settings.ActorClientId, form["actor_client_id"]);
			Assert.Equal("Żaneta & Jan +", form["id_hint"]);
			Assert.Equal(string.Join(',', fixture.Settings.DelegatedScopes), form["scope"]);
			return Json(System.Text.Json.JsonSerializer.Serialize(new { code }));
		};

		var authorization = new TinkAuthorizationService(fixture.Provider, fixture.Handler, Options.Create(fixture.Settings));
		var actualCode = await authorization.CreateDelegatedCodeAsync("existing-tink-user", "Żaneta & Jan +");
		var url = new TinkLinkUrlBuilder(Options.Create(fixture.Settings)).Build(actualCode);
		var parameters = QueryHelpers.ParseQuery(new Uri(url).Query);
		Assert.Equal(code, parameters["authorization_code"]);
		Assert.Equal(fixture.Settings.RedirectUri, parameters["redirect_uri"]);
		Assert.Equal("our-client", parameters["client_id"]);
		Assert.Equal("PL", parameters["market"]);
		Assert.Equal("en_US", parameters["locale"]);
		Assert.Equal(string.Join(',', fixture.Settings.RefreshableItems), parameters["refreshable_items"]);
		Assert.Equal(6, parameters.Count);
		Assert.DoesNotContain("private-client-secret", url);
		Assert.DoesNotContain("client-token", url);
		Assert.Equal(new[] { "/api/v1/oauth/token", "/api/v1/oauth/authorization-grant/delegate" }, calls);
	}

	[Fact]
	public async Task ConcurrentRequestsAcquireOnlyOneToken()
	{
		using var fixture = new Fixture();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var calls = 0;
		fixture.Handler.Respond = async (_, cancellationToken) =>
		{
			Interlocked.Increment(ref calls);
			entered.SetResult();
			await release.Task.WaitAsync(cancellationToken);
			return Json("{\"access_token\":\"cached-token\",\"expires_in\":1800}");
		};
		var first = fixture.Provider.GetTokenAsync();
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var others = Enumerable.Range(0, 20).Select(_ => fixture.Provider.GetTokenAsync()).ToArray();
		release.SetResult();
		Assert.All(await Task.WhenAll(others.Append(first)), token => Assert.Equal("cached-token", token));
		Assert.Equal("cached-token", await fixture.Provider.GetTokenAsync());
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task ExpirationUsesAbsoluteLifetimeWithSafetyMargin()
	{
		using var fixture = new Fixture();
		var calls = 0;
		fixture.Handler.Respond = (_, _) => Task.FromResult(Json(
			$"{{\"access_token\":\"token-{++calls}\",\"expires_in\":100}}"));
		Assert.Equal("token-1", await fixture.Provider.GetTokenAsync());
		fixture.Clock.Advance(TimeSpan.FromSeconds(89));
		Assert.Equal("token-1", await fixture.Provider.GetTokenAsync());
		fixture.Clock.Advance(TimeSpan.FromSeconds(1));
		Assert.Equal("token-2", await fixture.Provider.GetTokenAsync());
	}

	[Fact]
	public async Task EvictionCausesTokenToBeFetchedAgain()
	{
		using var fixture = new Fixture();
		var calls = 0;
		fixture.Handler.Respond = (_, _) => Task.FromResult(Json(
			$"{{\"access_token\":\"token-{++calls}\",\"expires_in\":1800}}"));
		await fixture.Provider.GetTokenAsync();
		fixture.Cache.Compact(1);
		Assert.Equal("token-2", await fixture.Provider.GetTokenAsync());
	}

	[Theory]
	[InlineData(400, "invalid_request")]
	[InlineData(401, "unauthorized")]
	[InlineData(403, "forbidden")]
	[InlineData(500, "upstream_error")]
	public async Task TokenErrorsAreSafeAndNotCached(int status, string error)
	{
		using var fixture = new Fixture();
		fixture.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
		{
			Content = new StringContent("private-client-secret client-token")
		});
		var exception = await Assert.ThrowsAsync<TinkApiException>(() => fixture.Provider.GetTokenAsync());
		Assert.Equal(error, exception.ErrorCode);
		Assert.Equal("client_token", exception.Stage);
		Assert.Equal((HttpStatusCode)status, exception.UpstreamStatus);
		Assert.DoesNotContain("private-client-secret", exception.ToString());
		Assert.DoesNotContain("client-token", exception.ToString());
		fixture.Handler.Respond = (_, _) => Task.FromResult(Json("{\"access_token\":\"valid\",\"expires_in\":1800}"));
		Assert.Equal("valid", await fixture.Provider.GetTokenAsync());
	}

	[Theory]
	[InlineData("not-json")]
	[InlineData("null")]
	[InlineData("{}")]
	[InlineData("{\"access_token\":\"\",\"expires_in\":1800}")]
	[InlineData("{\"access_token\":\"value\",\"expires_in\":0}")]
	[InlineData("{\"access_token\":\"value\",\"expires_in\":-1}")]
	[InlineData("{\"access_token\":\"value\",\"expires_in\":9223372036854775807}")]
	public async Task InvalidTokenResponseIsRejected(string body)
	{
		using var fixture = new Fixture();
		fixture.Handler.Respond = (_, _) => Task.FromResult(Json(body));
		var exception = await Assert.ThrowsAsync<TinkApiException>(() => fixture.Provider.GetTokenAsync());
		Assert.Equal("invalid_response", exception.ErrorCode);
	}

	[Theory]
	[InlineData(400, "invalid_request")]
	[InlineData(401, "unauthorized")]
	[InlineData(403, "forbidden")]
	public async Task DelegationErrorsAreSafeAndNotRetried(int status, string error)
	{
		using var fixture = new Fixture();
		var calls = 0;
		fixture.Handler.Respond = (request, _) =>
		{
			calls++;
			return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
				? Json("{\"access_token\":\"private-token\",\"expires_in\":1800}")
				: new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("private-code private-token") });
		};
		var service = new TinkAuthorizationService(fixture.Provider, fixture.Handler, Options.Create(fixture.Settings));
		var exception = await Assert.ThrowsAsync<TinkApiException>(() => service.CreateDelegatedCodeAsync("user", "name"));
		Assert.Equal(error, exception.ErrorCode);
		Assert.Equal("delegated_authorization", exception.Stage);
		Assert.DoesNotContain("private-token", exception.ToString());
		Assert.DoesNotContain("private-code", exception.ToString());
		Assert.Equal(2, calls);
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("null")]
	[InlineData("{\"code\":\" \"}")]
	[InlineData("invalid-json")]
	public async Task EmptyOrMalformedDelegatedCodeIsRejected(string body)
	{
		using var fixture = new Fixture();
		fixture.Handler.Respond = (request, _) => Task.FromResult(Json(
			request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
				? "{\"access_token\":\"token\",\"expires_in\":1800}" : body));
		var service = new TinkAuthorizationService(fixture.Provider, fixture.Handler, Options.Create(fixture.Settings));
		var exception = await Assert.ThrowsAsync<TinkApiException>(() => service.CreateDelegatedCodeAsync("user", "name"));
		Assert.Equal("invalid_response", exception.ErrorCode);
	}

	[Fact]
	public async Task NetworkFailuresDoNotExposeUnderlyingMessage()
	{
		using var fixture = new Fixture();
		fixture.Handler.Respond = (_, _) => throw new HttpRequestException("secret in URL");
		var exception = await Assert.ThrowsAsync<TinkApiException>(() => fixture.Provider.GetTokenAsync());
		Assert.Equal("network_error", exception.ErrorCode);
		Assert.DoesNotContain("secret in URL", exception.ToString());
	}

	[Fact]
	public async Task TimeoutIsDistinguishedFromCallerCancellation()
	{
		using var fixture = new Fixture();
		fixture.Handler.Respond = (_, _) => throw new TaskCanceledException();
		var exception = await Assert.ThrowsAsync<TinkApiException>(() => fixture.Provider.GetTokenAsync());
		Assert.Equal("timeout", exception.ErrorCode);
		using var cancellation = new CancellationTokenSource();
		fixture.Handler.Respond = (_, _) =>
		{
			cancellation.Cancel();
			throw new OperationCanceledException(cancellation.Token);
		};
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider.GetTokenAsync(cancellation.Token));
		fixture.Handler.Respond = (_, _) => Task.FromResult(Json("{\"access_token\":\"valid\",\"expires_in\":1800}"));
		Assert.Equal("valid", await fixture.Provider.GetTokenAsync());
	}

	[Fact]
	public void ConfigurationRejectsMissingCredentialsAndInvalidItems()
	{
		var settings = new TinkOptions();
		Assert.False(settings.IsValid());
		settings.ClientId = "client";
		settings.ClientSecret = "secret";
		settings.RedirectUri = "https://app.example/callback";
		Assert.True(settings.IsValid());
		settings.RefreshableItems = ["INVALID"];
		Assert.False(settings.IsValid());
	}

	[Fact]
	public void DependencyInjectionRegistersSharedProviderAndValidatedOptions()
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Tink:ClientId"] = "client",
			["Tink:ClientSecret"] = "secret",
			["Tink:RedirectUri"] = "https://app.example/callback",
			["Tink:ClientScopes:0"] = "authorization:grant",
			["Tink:RefreshableItems:0"] = "CHECKING_ACCOUNTS"
		}).Build();
		using var services = new ServiceCollection().AddLogging().AddTink(configuration).BuildServiceProvider();
		using var scope = services.CreateScope();
		Assert.Same(services.GetRequiredService<TinkClientTokenProvider>(),
			scope.ServiceProvider.GetRequiredService<TinkClientTokenProvider>());
		var settings = services.GetRequiredService<IOptions<TinkOptions>>().Value;
		Assert.True(settings.IsValid());
		Assert.Equal(new[] { "authorization:grant" }, settings.ClientScopes);
		Assert.Equal(new[] { "CHECKING_ACCOUNTS" }, settings.RefreshableItems);
		Assert.NotNull(services.GetRequiredService<TinkAuthorizationService>());
		Assert.NotNull(services.GetRequiredService<TinkLinkUrlBuilder>());
	}

	private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
	{
		Content = new StringContent(body, Encoding.UTF8, "application/json")
	};

	private sealed class Fixture : IDisposable
	{
		public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
		public FakeClock Clock { get; } = new();
		public FakeHttp Handler { get; } = new();
		public TinkOptions Settings { get; } = new()
		{
			ClientId = "client",
			ClientSecret = "secret",
			RedirectUri = "https://app.example/callback"
		};
		public TinkClientTokenProvider Provider { get; }
		public Fixture() => Provider = new(Cache, Handler, Options.Create(Settings), Clock);
		public void Dispose()
		{
			Provider.Dispose();
			Cache.Dispose();
			Handler.Dispose();
		}
	}

	private sealed class FakeClock : TimeProvider
	{
		private DateTimeOffset _now = DateTimeOffset.UtcNow;
		public override DateTimeOffset GetUtcNow() => _now;
		public void Advance(TimeSpan elapsed) => _now += elapsed;
	}

	private sealed class FakeHttp : HttpMessageHandler, IHttpClientFactory
	{
		public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; }
			= (_, _) => throw new InvalidOperationException("Unexpected HTTP request.");
		public HttpClient CreateClient(string name) => new(this, disposeHandler: false)
		{
			BaseAddress = new Uri("https://api.tink.com/")
		};
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Respond(request, cancellationToken);
	}
}
