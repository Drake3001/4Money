using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;
using ZPI4M.Banking.Tink.Http;

namespace ZPI4M.Banking.UnitTests;

public sealed partial class TinkTests
{
	[Fact]
	public async Task ConsentsUseUserGrantAndNeverCacheUserTokenOrConsentResponse()
	{
		using var fixture = new Fixture();
		fixture.Settings.ContinuousAccessVerified = true;
		var paths = new List<string>();
		var scopeRequests = 0;
		fixture.Handler.Respond = async (request, ct) =>
		{
			var path = request.RequestUri!.AbsolutePath;
			paths.Add(path);
			Assert.Equal("https://api.tink.com", request.RequestUri.GetLeftPart(UriPartial.Authority));
			switch (path)
			{
				case "/api/v1/oauth/authorization-grant":
					var grant = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
					Assert.Equal("owner", grant["user_id"]);
					Assert.Equal("provider-consents:read", grant["scope"]);
					Assert.Equal(2, grant.Count);
					Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
					Assert.Equal("client-token", request.Headers.Authorization.Parameter);
					return Json("{\"code\":\"user-code\"}");
				case "/api/v1/oauth/token":
					var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
					if (form["grant_type"] == "client_credentials")
					{
						scopeRequests++;
						Assert.Equal("authorization:grant", form["scope"]);
						return Json("{\"access_token\":\"client-token\",\"expires_in\":1800}");
					}
					Assert.Equal("authorization_code", form["grant_type"]);
					Assert.Equal("user-code", form["code"]);
					Assert.Equal(fixture.Settings.ClientId, form["client_id"]);
					Assert.Equal(fixture.Settings.ClientSecret, form["client_secret"]);
					return Json("{\"access_token\":\"user-token\",\"expires_in\":1800}");
				case "/api/v1/provider-consents":
					Assert.Equal(HttpMethod.Get, request.Method);
					Assert.Empty(request.RequestUri.Query);
					Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
					Assert.Equal("user-token", request.Headers.Authorization.Parameter);
					return Json("""
						{"providerConsents":[
						 {"credentialsId":"bank-a","status":"UPDATED","sessionExpiryDate":1796126400000,"sessionExtendable":false,"accountIds":["account-a","account-b"]},
						 {"credentialsId":"bank-b","status":"FUTURE_PROVIDER_STATUS"}
						]}
						""");
				default:
					throw new InvalidOperationException("Unexpected endpoint.");
			}
		};
		var gateway = Gateway(fixture);
		for (var i = 0; i < 2; i++)
		{
			var consents = await gateway.GetConsentsAsync("owner", default);
			Assert.Equal(2, consents.Count);
			Assert.Equal("bank-a", consents[0].CredentialsId);
			Assert.Equal("UPDATED", consents[0].ProviderStatus);
			Assert.Equal(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero), consents[0].SessionExpiresAt);
			Assert.False(consents[0].SessionExtendable);
			Assert.Equal("FUTURE_PROVIDER_STATUS", consents[1].ProviderStatus);
			Assert.Null(consents[1].SessionExpiresAt);
			Assert.Null(consents[1].SessionExtendable);
		}
		Assert.Equal(1, scopeRequests);
		Assert.Equal(2, paths.Count(path => path == "/api/v1/provider-consents"));
		Assert.Equal(2, paths.Count(path => path == "/api/v1/oauth/authorization-grant"));
		Assert.Equal(7, paths.Count);
	}

	[Theory]
	[InlineData("{\"providerConsents\":[]}", 0)]
	[InlineData("{\"providerConsents\":[{\"credentialsId\":\"id\",\"status\":\"UPDATED\",\"sessionExpiryDate\":null,\"sessionExtendable\":null}]}", 1)]
	public async Task ConsentsSupportEmptyListsAndNullableFields(string body, int count)
	{
		using var fixture = new Fixture();
		ConfigureConsentResponse(fixture, () => Json(body));
		var consents = await Gateway(fixture).GetConsentsAsync("owner", default);
		Assert.Equal(count, consents.Count);
		Assert.All(consents, consent =>
		{
			Assert.Null(consent.SessionExpiresAt);
			Assert.Null(consent.SessionExtendable);
		});
	}

	[Theory]
	[InlineData("not-json")]
	[InlineData("null")]
	[InlineData("{}")]
	[InlineData("{\"providerConsents\":null}")]
	[InlineData("{\"providerConsents\":[null]}")]
	[InlineData("{\"providerConsents\":[{}]}")]
	[InlineData("{\"providerConsents\":[{\"credentialsId\":\"id\"}]}")]
	[InlineData("{\"providerConsents\":[{\"credentialsId\":\" \",\"status\":\"UPDATED\"}]}")]
	[InlineData("{\"providerConsents\":[{\"credentialsId\":\"id\",\"status\":\"UPDATED\",\"sessionExpiryDate\":9223372036854775807}]}")]
	[InlineData("{\"providerConsents\":[{\"credentialsId\":\"id\",\"status\":\"UPDATED\",\"sessionExpiryDate\":\"bad-date\"}]}")]
	[InlineData("{\"providerConsents\":[{\"credentialsId\":\"id\",\"status\":\"UPDATED\"},null]}")]
	public async Task InvalidConsentResponsesFailWithoutPartialResults(string body)
	{
		using var fixture = new Fixture();
		ConfigureConsentResponse(fixture, () => Json(body));
		var error = await Assert.ThrowsAsync<TinkApiException>(() => Gateway(fixture).GetConsentsAsync("owner", default));
		Assert.Equal("provider_consents", error.Stage);
		Assert.Equal("invalid_response", error.ErrorCode);
	}

	[Theory]
	[InlineData(400, "invalid_request")]
	[InlineData(401, "unauthorized")]
	[InlineData(403, "forbidden")]
	[InlineData(500, "upstream_error")]
	public async Task ConsentHttpErrorsDoNotLeakResponsesOrRetry(int status, string code)
	{
		using var fixture = new Fixture();
		var calls = 0;
		ConfigureConsentResponse(fixture, () =>
		{
			calls++;
			return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("private-provider-response") };
		});
		var error = await Assert.ThrowsAsync<TinkApiException>(() => Gateway(fixture).GetConsentsAsync("owner", default));
		Assert.Equal(code, error.ErrorCode);
		Assert.DoesNotContain("private-provider-response", error.ToString());
		Assert.Equal(1, calls);
	}

	[Theory]
	[InlineData(false, "network_error")]
	[InlineData(true, "timeout")]
	public async Task ConsentTransportErrorsAreSafe(bool timeout, string code)
	{
		using var fixture = new Fixture();
		ConfigureConsentResponse(fixture, () => timeout
		 ? throw new TaskCanceledException("private-provider-response")
		 : throw new HttpRequestException("private-provider-response"));
		var error = await Assert.ThrowsAsync<TinkApiException>(() => Gateway(fixture).GetConsentsAsync("owner", default));
		Assert.Equal(code, error.ErrorCode);
		Assert.DoesNotContain("private-provider-response", error.ToString());
	}

	[Fact]
	public async Task UnverifiedProductCannotReadConsents()
	{
		using var fixture = new Fixture();
		var error = await Assert.ThrowsAsync<TinkApiException>(() => Gateway(fixture).GetConsentsAsync("owner", default));
		Assert.Equal("continuous_access_unverified", error.ErrorCode);
	}

	private static void ConfigureConsentResponse(Fixture fixture, Func<HttpResponseMessage> respond)
	{
		fixture.Settings.ContinuousAccessVerified = true;
		fixture.Handler.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
		{
			"/api/v1/oauth/authorization-grant" => Json("{\"code\":\"code\"}"),
			"/api/v1/oauth/token" => Json("{\"access_token\":\"token\",\"expires_in\":1800}"),
			"/api/v1/provider-consents" => respond(),
			_ => throw new InvalidOperationException("Unexpected endpoint.")
		});
	}
}
