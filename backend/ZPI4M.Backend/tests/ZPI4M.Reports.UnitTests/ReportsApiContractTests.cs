using System.Net;
using System.Net.Http.Json;
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
using ZPI4M.Api.OpenApi;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Reports.ApiControllers;
using ZPI4M.Reports.Services.Reports;
using ZPI4M.Reports.TestSupport;
using ZPI4M.Shared.CurrentUser;
using static ZPI4M.Reports.TestSupport.ReportData;

namespace ZPI4M.Reports.UnitTests;

public sealed class ReportsApiContractTests
{
	private static readonly string[] Routes = ["category-breakdown", "period-category-totals", "day-of-week-totals", "daily-totals"];
	private static string Query => $"dateFrom=2026-11-01&dateTo=2026-11-30&scope=personal&categoryIds={Food}";

	[Fact]
	public async Task AllRoutesRequireAuthenticationAndValidCurrentUser()
	{
		await using var host = await Host.StartAsync();
		foreach (var actor in new[] { null, "invalid", Guid.Empty.ToString() })
		{
			host.Client.DefaultRequestHeaders.Remove("X-Test-User");
			if (actor is not null) host.Client.DefaultRequestHeaders.Add("X-Test-User", actor);
			foreach (var route in Routes)
				Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync($"/api/reports/{route}?{Query}")).StatusCode);
		}
		Assert.Equal(0, host.Source.Calls);
	}

	[Theory]
	[InlineData("category-breakdown")]
	[InlineData("period-category-totals")]
	[InlineData("day-of-week-totals")]
	[InlineData("daily-totals")]
	public async Task RealServiceBindsPersonalGroupAndMissingCategorySelections(string route)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", Actor.ToString());
		foreach (var group in new[] { false, true })
		{
			var query = Query.Replace("scope=personal", group ? $"scope=group&groupId={Group}" : "scope=personal") +
				$"&categoryIds={Travel}&actor={Guid.NewGuid()}";
			using var response = await host.Client.GetAsync($"/api/reports/{route}?{query}");
			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.True(response.Headers.CacheControl?.NoStore);
			Assert.Equal(Actor, host.Source.Actor);
			Assert.Equal(group ? Group : (Guid?)null, host.Source.GroupId);
			Assert.Equal(group ? ExpenseAggregationScope.Group : ExpenseAggregationScope.Personal, host.Source.Scope);
			Assert.Equal(new[] { Food, Travel }, host.Source.CategoryIds);
			var body = await response.Content.ReadFromJsonAsync<JsonElement>();
			if (route == "period-category-totals")
			{
				Assert.Equal("Month", body.GetProperty("granularity").GetString());
				Assert.Equal(2, body.GetProperty("series").GetArrayLength());
			}
			else
			{
				var items = body.GetProperty("items");
				Assert.Equal(route == "day-of-week-totals" ? 7 : route == "daily-totals" ? 30 : 1, items.GetArrayLength());
				Assert.Equal(JsonValueKind.Number, items[0].GetProperty(route == "day-of-week-totals" ? "averageAmount" : "totalAmount").ValueKind);
			}
		}
		using var empty = await host.Client.GetAsync($"/api/reports/{route}?dateFrom=2026-11-01&dateTo=2026-11-30&scope=personal");
		Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
		Assert.Empty(host.Source.CategoryIds!);
		var json = await empty.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(0, json.GetProperty(route == "period-category-totals" ? "series" : "items").GetArrayLength());
	}

	[Theory]
	[InlineData("category-breakdown")]
	[InlineData("period-category-totals")]
	[InlineData("day-of-week-totals")]
	[InlineData("daily-totals")]
	public async Task StandardBindingAcceptsSingleAndIndexedCategoryIds(string route)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", Actor.ToString());
		foreach (var query in new[] { Query, Query.Replace($"categoryIds={Food}", $"categoryIds[0]={Food}") })
		{
			using var response = await host.Client.GetAsync($"/api/reports/{route}?{query}");
			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.Equal(new[] { Food }, host.Source.CategoryIds);
		}
	}

	public static IEnumerable<object[]> InvalidQueries()
	{
		string[] queries = [
			$"dateTo=2026-11-30&scope=personal&categoryIds={Food}",
			$"dateFrom=2026-11-01&scope=personal&categoryIds={Food}",
			$"dateFrom=2026-11-01&dateTo=2026-11-30&categoryIds={Food}",
			Query.Replace("2026-11-01", "2026-12-01"),
			Query.Replace("2026-11-01", "11%2F01%2F2026"),
			Query + "&dateFrom=2026-11-02",
			Query.Replace("scope=personal", "scope=0"),
			Query.Replace("scope=personal", "scope=Personal"),
			Query.Replace("scope=personal", "scope=unknown"),
			Query + "&scope=group",
			Query + $"&groupId={Group}",
			Query.Replace("scope=personal", "scope=group"),
			Query.Replace("scope=personal", "scope=group&groupId="),
			Query.Replace("scope=personal", $"scope=group&groupId={Guid.Empty}"),
			Query.Replace("scope=personal", $"scope=group&groupId={Group}&groupId={Group}"),
			Query.Replace(Food.ToString(), "abc"),
			Query.Replace(Food.ToString(), ""),
			Query.Replace(Food.ToString(), Guid.Empty.ToString()),
			Query.Replace(Food.ToString(), "%20"),
			Query + "&categoryIds=",
			Query.Replace(Food.ToString(), "") + $"&categoryIds={Food}",
			Query.Replace(Food.ToString(), "") + "&categoryIds=",
			Query.Replace(Food.ToString(), $"{Food},{Travel}")
		];
		foreach (var route in Routes)
			foreach (var query in queries)
				yield return [route, query];
	}

	public static IEnumerable<object[]> DateQueries()
	{
		(string From, string To, int Status, string? Field)[] dates = [
			("1969-12-31", "1970-01-01", 400, "dateFrom"),
			("1970-01-01", "1969-12-31", 400, "dateTo"),
			("1969-12-30", "1969-12-31", 400, "dateFrom"),
			("0001-01-01", "1970-01-01", 400, "dateFrom"),
			("2026-11-30", "2026-11-01", 400, "dateFrom"),
			("1970-01-01", "1970-01-01", 200, null),
			("2018-01-01", "2026-12-31", 200, null),
			("1970-01-01", "2060-12-31", 200, null),
			("9999-12-31", "9999-12-31", 200, null),
			("0000-01-01", "1970-01-01", 400, "dateFrom"),
			("1970-01-01", "10000-01-01", 400, "dateTo")
		];
		foreach (var route in Routes)
			foreach (var date in dates)
				yield return [route, date.From, date.To, date.Status, date.Field!];
	}

	[Theory]
	[MemberData(nameof(DateQueries))]
	public async Task AllRoutesApplySharedFullDateRules(string route, string from, string to, int status, string? field)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", Actor.ToString());
		using var response = await host.Client.GetAsync($"/api/reports/{route}?dateFrom={from}&dateTo={to}&scope=personal&categoryIds={Food}");
		Assert.Equal(status, (int)response.StatusCode);
		if (status == 400)
		{
			Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
			var body = await response.Content.ReadFromJsonAsync<JsonElement>();
			Assert.Contains(body.GetProperty("errors").EnumerateObject(),
				error => string.Equals(error.Name, field, StringComparison.OrdinalIgnoreCase));
			Assert.Equal(0, host.Source.Calls);
		}
		else
		{
			Assert.Equal(1, host.Source.Calls);
			Assert.Equal(DateOnly.ParseExact(from, "yyyy-MM-dd"), host.Source.From);
			Assert.Equal(DateOnly.ParseExact(to, "yyyy-MM-dd"), host.Source.To);
		}
	}

	[Theory]
	[MemberData(nameof(InvalidQueries))]
	public async Task InvalidQueriesReturnProblemsBeforeExpense(string route, string query)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", Actor.ToString());
		using var response = await host.Client.GetAsync($"/api/reports/{route}?{query}");
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		var body = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.NotEmpty(body.GetProperty("errors").EnumerateObject());
		Assert.Equal(0, host.Source.Calls);
	}

	[Fact]
	public async Task RemovedAliasesAreNotRoutable()
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", Actor.ToString());
		foreach (var old in new[] { "monthly-totals", "spending-trend" })
			Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/reports/{old}?{Query}")).StatusCode);
	}

	[Theory]
	[InlineData(ExpenseErrorCode.NotFound, 404)]
	[InlineData(ExpenseErrorCode.Forbidden, 403)]
	[InlineData(ExpenseErrorCode.Conflict, 409)]
	[InlineData(ExpenseErrorCode.InvalidExpense, 400)]
	[InlineData(ExpenseErrorCode.TemporarilyUnavailable, 503)]
	public async Task SourceErrorsMapToHttpEvenForEmptySelection(ExpenseErrorCode code, int status)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", Actor.ToString());
		host.Source.Error = code;
		foreach (var route in Routes)
		{
			using var response = await host.Client.GetAsync($"/api/reports/{route}?{Query.Replace($"&categoryIds={Food}", "")}");
			Assert.Equal(status, (int)response.StatusCode);
			Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
			if (status == 400)
				Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("dateFrom", out _));
		}
	}

	[Fact]
	public async Task TransportDiagnosticsAreNotExposed()
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", Actor.ToString());
		host.Source.TransportFailure = true;
		foreach (var route in Routes)
		{
			using var response = await host.Client.GetAsync($"/api/reports/{route}?{Query}");
			Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
			Assert.DoesNotContain("synthetic-private-diagnostic", await response.Content.ReadAsStringAsync());
		}
	}

	[Fact]
	public async Task GeneratedOpenApiHasFourRoutesAndOptionalGuidArraySchema()
	{
		await using var host = await Host.StartAsync();
		var document = await host.Client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
		var paths = document.GetProperty("paths");
		Assert.Equal(Routes.Order(), paths.EnumerateObject().Select(path => path.Name.Replace("/api/reports/", "")).Order());
		foreach (var route in Routes)
		{
			var operation = paths.GetProperty("/api/reports/" + route).GetProperty("get");
			var parameters = operation.GetProperty("parameters").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
			Assert.Equal(5, parameters.Count);
			Assert.False(parameters.ContainsKey("actor"));
			Assert.False(parameters.ContainsKey("granularity"));
			Assert.Contains("1970-01-01", parameters["dateFrom"].GetProperty("description").GetString());
			Assert.Contains("1970-01-01", parameters["dateTo"].GetProperty("description").GetString());
			var category = parameters["categoryIds"];
			Assert.True(!category.TryGetProperty("required", out var required) || !required.GetBoolean());
			// OpenAPI omits default query form/explode settings during serialization.
			Assert.Equal("form", category.TryGetProperty("style", out var style) ? style.GetString() : "form");
			Assert.True(!category.TryGetProperty("explode", out var explode) || explode.GetBoolean());
			var schema = category.GetProperty("schema");
			Assert.Equal("array", schema.GetProperty("type").GetString());
			Assert.Equal("uuid", schema.GetProperty("items").GetProperty("format").GetString());
			Assert.False(schema.TryGetProperty("oneOf", out _));
			Assert.Equal(0, category.GetProperty("examples").GetProperty("empty").GetProperty("value").GetArrayLength());
			Assert.Equal(new[] { "personal", "group" }, parameters["scope"].GetProperty("schema").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
			Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
		}
	}

	private sealed class Host(WebApplication app, HttpClient client, ReportsSourceStub source) : IAsyncDisposable
	{
		public HttpClient Client { get; } = client;
		public ReportsSourceStub Source { get; } = source;
		public static async Task<Host> StartAsync()
		{
			var builder = WebApplication.CreateBuilder();
			builder.WebHost.UseTestServer();
			builder.Logging.ClearProviders();
			var mvc = builder.Services.AddControllers();
			mvc.PartManager.ApplicationParts.Clear();
			mvc.AddApplicationPart(typeof(ReportsController).Assembly);
			builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, context, _) =>
			{
				ReportsOpenApi.Configure(operation, context.Description.ActionDescriptor.AttributeRouteInfo?.Name);
				return Task.CompletedTask;
			}));
			builder.Services.AddProblemDetails();
			builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
			var source = new ReportsSourceStub
			{
				Categories = [new(Food, 12.34m)],
				Periods = [new(new(2026, 11, 1), Food, 12.34m)],
				Days = [new(new(2026, 11, 1), 12.34m)],
				Weekdays = [new(DayOfWeek.Sunday, 12.34m)]
			};
			builder.Services.AddSingleton<IExpenseAggregationService>(source);
			builder.Services.AddSingleton(Metadata(Food, Travel));
			builder.Services.AddScoped<IReportsService, ReportsService>();
			var app = builder.Build();
			app.UseAuthentication();
			app.UseAuthorization();
			app.MapControllers();
			app.MapOpenApi();
			await app.StartAsync();
			return new(app, app.GetTestClient(), source);
		}
		public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
	}

	private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue("X-Test-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
			var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], Scheme.Name));
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
		}
	}
}
