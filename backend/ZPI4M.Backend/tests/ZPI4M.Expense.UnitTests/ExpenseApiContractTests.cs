using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Expense.ApiControllers;
using ZPI4M.Expense.DTOs.Banking;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.DTOs.Receipts;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.UnitTests;

public sealed class ExpenseApiContractTests
{
	private const string UserId = "10000000-0000-0000-0000-000000000001";
	private const string ExpenseId = "70000000-0000-0000-0000-000000000001";
	private const string CreateJson = """
		{
		  "groupId": "20000000-0000-0000-0000-000000000001",
		  "payerId": "10000000-0000-0000-0000-000000000001",
		  "name": "Synthetic purchase", "amount": "100.00",
		  "transactionTime": "2026-11-02", "items": [],
		  "shares": [{ "userId": "10000000-0000-0000-0000-000000000001", "percentage": "100.00" }],
		  "bankTransactionId": null
		}
		""";
	private static readonly (string Method, string Path)[] Routes =
	[
		("GET", "/api/categories"),
		("GET", "/api/expenses"),
		("GET", $"/api/expenses/{ExpenseId}"),
		("POST", "/api/expenses/preview"),
		("POST", "/api/expenses"),
		("PUT", $"/api/expenses/{ExpenseId}"),
		("DELETE", $"/api/expenses/{ExpenseId}?version=opaque-v1"),
		("GET", "/api/expenses/available-bank-transactions"),
		("GET", $"/api/expenses/{ExpenseId}/receipt"),
		("POST", "/api/expenses/receipts/analyze")
	];

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task MultipartActionsUseTheSameJsonValidation(bool update)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", UserId);
		var body = update ? UpdateBody() : JsonNode.Parse(CreateJson)!.AsObject();
		foreach (var missingDate in new[] { false, true })
		{
			var value = body.DeepClone().AsObject();
			if (missingDate) value.Remove("transactionTime");
			using var request = new HttpRequestMessage(update ? HttpMethod.Put : HttpMethod.Post,
				update ? $"/api/expenses/{ExpenseId}" : "/api/expenses");
			var form = new MultipartFormDataContent();
			request.Content = form;
			form.Add(new StringContent(value.ToJsonString()), "expense");
			form.Add(new ByteArrayContent([1]), "file", "synthetic.png");
			using var response = await host.Client.SendAsync(request);
			Assert.Equal(missingDate ? HttpStatusCode.BadRequest : HttpStatusCode.ServiceUnavailable, response.StatusCode);
		}
	}

	[Fact]
	public async Task AllRoutesRequireAuthenticationAndKeepPendingOperationsSeparate()
	{
		await using var host = await Host.StartAsync();
		foreach (var (method, path) in Routes)
		{
			using var request = Request(method, path);
			using var response = await host.Client.SendAsync(request);
			Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		}

		host.Client.DefaultRequestHeaders.Add("X-Test-User", UserId);
		foreach (var (method, path) in Routes)
		{
			using var request = Request(method, path);
			using var response = await host.Client.SendAsync(request);
			Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
			Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
			Assert.True(response.Headers.CacheControl?.NoStore);
			var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
			Assert.Equal((int)response.StatusCode, problem.GetProperty("status").GetInt32());
		}
	}

	[Fact]
	public async Task ReceiptAnalysisLimitsEachUserIndependently()
	{
		await using var host = await Host.StartAsync();
		for (var attempt = 0; attempt < 6; attempt++)
		{
			using var request = Request("POST", "/api/expenses/receipts/analyze");
			request.Headers.Add("X-Test-User", UserId);
			using var response = await host.Client.SendAsync(request);
			Assert.Equal(attempt < 5 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.TooManyRequests, response.StatusCode);
		}

		using var anotherUser = Request("POST", "/api/expenses/receipts/analyze");
		anotherUser.Headers.Add("X-Test-User", "10000000-0000-0000-0000-000000000002");
		using var allowed = await host.Client.SendAsync(anotherUser);
		Assert.Equal(HttpStatusCode.ServiceUnavailable, allowed.StatusCode);

		using var receipt = Request("GET", $"/api/expenses/{ExpenseId}/receipt");
		receipt.Headers.Add("X-Test-User", UserId);
		using var read = await host.Client.SendAsync(receipt);
		Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode);
	}

	[Fact]
	public void ExpenseRegistrationPreservesImplementedGroupingService()
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
		builder.Services.AddScoped<ZPI4M.Grouping.Services.Members.IGroupMemberService, ZPI4M.Grouping.Services.Members.GroupMemberService>();
		builder.Services.AddExpenseServices();
		var registration = Assert.Single(builder.Services, service => service.ServiceType == typeof(ZPI4M.Grouping.Services.Members.IGroupMemberService));
		Assert.Equal(typeof(ZPI4M.Grouping.Services.Members.GroupMemberService), registration.ImplementationType);
	}

	[Fact]
	public void ExpenseRegistrationLeavesOtherModulesToTheHost()
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
		builder.Services.AddExpenseServices();
		Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(ZPI4M.Grouping.Services.Members.IGroupMemberService));
		Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(ZPI4M.Grouping.Services.ExpenseMetadata.IGroupExpenseMetadataLookup));
		Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(ZPI4M.Grouping.Services.Expenses.IGroupExpenseContributionService));
		Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(ZPI4M.User.Services.Lookup.IUserDisplayNameLookup));
	}

	[Theory]
	[InlineData("invalid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public async Task InvalidCurrentUserStillReturns401(string userId)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", userId);
		using var response = await host.Client.GetAsync("/api/categories");
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Theory]
	[InlineData("/api/expenses?pageNumber=0")]
	[InlineData("/api/expenses?transactionTimeFrom=2026-11-02T12:00:00%2B01:00")]
	[InlineData("/api/expenses?pageSize=101")]
	[InlineData("/api/expenses?pageNumber=2147483647&pageSize=100")]
	[InlineData("/api/expenses/available-bank-transactions?pageSize=0")]
	[InlineData("/api/expenses/available-bank-transactions?sortBy=unknown")]
	public async Task InvalidPaginationAndSortReturnValidationProblem(string path)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", UserId);
		using var response = await host.Client.GetAsync(path);
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.True(problem.GetProperty("errors").EnumerateObject().Any());
	}

	[Theory]
	[InlineData("bankTransactionId")]
	[InlineData("version")]
	public async Task UpdateRequiresBothNullableSourceFieldsAndVersion(string missing)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", UserId);
		var body = UpdateBody();
		body.Remove(missing);
		using var response = await host.Client.PutAsJsonAsync($"/api/expenses/{ExpenseId}", body);
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task UpdateRejectsGroupChangesAndDeleteRequiresVersion()
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", UserId);
		var body = UpdateBody();
		body["groupId"] = "20000000-0000-0000-0000-000000000002";
		using var update = await host.Client.PutAsJsonAsync($"/api/expenses/{ExpenseId}", body);
		Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
		using var delete = await host.Client.DeleteAsync($"/api/expenses/{ExpenseId}");
		Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
	}

	[Fact]
	public async Task GeneratedOpenApiContainsAllDeclarationsAndDtoShapes()
	{
		await using var host = await Host.StartAsync();
		var doc = await host.Client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
		var paths = doc.GetProperty("paths");
		Assert.Equal(10, paths.EnumerateObject().Sum(path => path.Value.EnumerateObject()
			.Count(operation => operation.Name is "get" or "post" or "put" or "delete")));
		foreach (var (method, path) in Routes)
		{
			var template = path.Split('?')[0].Replace(ExpenseId, "{expenseId}");
			var responses = paths.GetProperty(template).GetProperty(method.ToLowerInvariant()).GetProperty("responses");
			Assert.True(responses.TryGetProperty("503", out _));

		}
		var schemas = doc.GetProperty("components").GetProperty("schemas");
		Assert.Equal("createExpense", paths.GetProperty("/api/expenses").GetProperty("post").GetProperty("operationId").GetString());
		foreach (var schema in new[] { "CreateExpenseRequest", "UpdateExpenseRequest", "PreviewExpenseRequest", "ExpenseResponse", "ExpenseListItemResponse" })
			Assert.Equal("date", schemas.GetProperty(schema).GetProperty("properties").GetProperty("transactionTime").GetProperty("format").GetString());
		var receiptDraft = schemas.GetProperty("ReceiptDraft").GetProperty("properties");
		Assert.Equal("date", receiptDraft.GetProperty("transactionTime").GetProperty("format").GetString());
		Assert.False(receiptDraft.TryGetProperty("transactionDate", out _));
		var update = schemas.GetProperty("UpdateExpenseRequest");
		Assert.False(update.GetProperty("properties").TryGetProperty("groupId", out _));
		var required = update.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
		Assert.Contains("version", required);
		Assert.Contains("bankTransactionId", required);
		Assert.DoesNotContain("receiptId", required);
		var receipt = schemas.GetProperty("ReceiptResponse").GetProperty("properties");
		Assert.True(receipt.TryGetProperty("analysis", out _));
		Assert.True(schemas.GetProperty("ExpenseResponse").GetProperty("properties").TryGetProperty("canEdit", out _));
		var upload = paths.GetProperty("/api/expenses/receipts/analyze").GetProperty("post").GetProperty("requestBody")
			.GetProperty("content").GetProperty("multipart/form-data");
		Assert.True(upload.TryGetProperty("schema", out _));
	}

	[Theory]
	[InlineData("2026-11-02", HttpStatusCode.ServiceUnavailable)]
	[InlineData("2026-11-02T10:00:00Z", HttpStatusCode.BadRequest)]
	[InlineData("2026-02-30", HttpStatusCode.BadRequest)]
	public async Task ExpenseBodiesAcceptOnlyCalendarDates(string date, HttpStatusCode expected)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", UserId);
		foreach (var (method, path) in new[] { ("POST", "/api/expenses"), ("POST", "/api/expenses/preview"), ("PUT", $"/api/expenses/{ExpenseId}") })
		{
			var body = method == "PUT" ? UpdateBody() : JsonNode.Parse(CreateJson)!.AsObject();
			body["transactionTime"] = date;
			using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(body) };
			using var response = await host.Client.SendAsync(request);
			Assert.Equal(expected, response.StatusCode);
		}
	}

	[Theory]
	[InlineData("2026-11-02", HttpStatusCode.ServiceUnavailable)]
	[InlineData("2026-11-02T00:00:00Z", HttpStatusCode.BadRequest)]
	[InlineData("11/02/2026", HttpStatusCode.BadRequest)]
	[InlineData("2026-02-30", HttpStatusCode.BadRequest)]
	public async Task HistoryFiltersAcceptOnlyIsoDates(string date, HttpStatusCode expected)
	{
		await using var host = await Host.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", UserId);
		foreach (var field in new[] { "transactionTimeFrom", "transactionTimeTo" })
		{
			using var response = await host.Client.GetAsync($"/api/expenses?{field}={Uri.EscapeDataString(date)}");
			Assert.Equal(expected, response.StatusCode);
		}
	}

	private static JsonObject UpdateBody()
	{
		var body = JsonNode.Parse(CreateJson)!.AsObject();
		body.Remove("groupId");
		body["version"] = "opaque-v1";
		return body;
	}

	private static HttpRequestMessage Request(string method, string path)
	{
		var request = new HttpRequestMessage(new HttpMethod(method), path);
		if (method == "POST" && path == "/api/expenses/receipts/analyze")
		{
			var multipart = new MultipartFormDataContent();
			multipart.Add(new ByteArrayContent([1]), "file", "synthetic.jpg");
			request.Content = multipart;
		}
		else if (method == "PUT") request.Content = JsonContent.Create(UpdateBody());
		else if (method == "POST" && !path.EndsWith("/analyze", StringComparison.Ordinal))
			request.Content = JsonContent.Create(JsonSerializer.Deserialize<JsonElement>(CreateJson));
		return request;
	}

	private sealed class UnavailableExpenseService : IExpenseService
	{
		public Task<ExpenseResult<PagedResult<ExpenseHistoryEntry>>> ListAsync(Guid actor, ExpenseListRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(ExpenseResult<PagedResult<ExpenseHistoryEntry>>.Failure(ExpenseErrorCode.TemporarilyUnavailable));
		public Task<ExpenseResult<ExpenseView>> UpdateAsync(Guid actor, Guid expenseId, UpdateExpenseRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(ExpenseResult<ExpenseView>.Failure(ExpenseErrorCode.TemporarilyUnavailable));
		public Task<ExpenseResult<bool>> DeleteAsync(Guid actor, Guid expenseId, string version, CancellationToken cancellationToken = default) =>
			Task.FromResult(ExpenseResult<bool>.Failure(ExpenseErrorCode.TemporarilyUnavailable));
		public Task<ExpenseResult<ReceiptResponse>> GetReceiptAsync(Guid actor, Guid receiptId, CancellationToken cancellationToken) =>
			Task.FromResult(ExpenseResult<ReceiptResponse>.Failure(ExpenseErrorCode.TemporarilyUnavailable));

		public Task<ExpenseResult<ReceiptOcrResponse>> AnalyzeReceiptAsync(Guid actor, IFormFile file, CancellationToken cancellationToken) =>
			Task.FromResult(ExpenseResult<ReceiptOcrResponse>.Failure(ExpenseErrorCode.TemporarilyUnavailable));

		public Task<ExpenseResult<IReadOnlyList<CategoryDetails>>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(ExpenseResult<IReadOnlyList<CategoryDetails>>.Failure(ExpenseErrorCode.TemporarilyUnavailable));
		public Task<ExpenseResult<ExpenseView>> CreateAsync(Guid actor, CreateExpenseRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(ExpenseResult<ExpenseView>.Failure(ExpenseErrorCode.TemporarilyUnavailable));
		public Task<ExpenseResult<ExpenseView>> GetByIdAsync(Guid actor, Guid expenseId, CancellationToken cancellationToken = default) =>
			Task.FromResult(ExpenseResult<ExpenseView>.Failure(ExpenseErrorCode.TemporarilyUnavailable));
		public Task<ExpenseResult<ExpensePreview>> PreviewAsync(Guid actor, PreviewExpenseRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(ExpenseResult<ExpensePreview>.Failure(ExpenseErrorCode.TemporarilyUnavailable));
		public Task<ExpenseResult<PagedResult<BankTransactionDetails>>> GetAvailableBankTransactionsAsync(Guid actor, AvailableBankTransactionRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(ExpenseResult<PagedResult<BankTransactionDetails>>.Failure(ExpenseErrorCode.TemporarilyUnavailable));
	}

	private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
	{
		public HttpClient Client { get; } = client;

		public static async Task<Host> StartAsync()
		{
			var builder = WebApplication.CreateBuilder();
			builder.WebHost.UseTestServer();
			var mvc = builder.Services.AddControllers();
			mvc.PartManager.ApplicationParts.Clear();
			mvc.AddApplicationPart(typeof(ExpensesController).Assembly);
			builder.Services.AddOpenApi();
			builder.Services.AddProblemDetails();
			builder.Services.AddScoped<IExpenseService, UnavailableExpenseService>();
			builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
			builder.Services.AddExpenseOcrRateLimiting();
			// Deliberately no database, storage, OCR, repositories or business services.
			var app = builder.Build();
			app.UseAuthentication();
			app.UseRateLimiter();
			app.UseAuthorization();
			app.MapControllers();
			app.MapOpenApi();
			await app.StartAsync();
			return new(app, app.GetTestClient());
		}

		public async ValueTask DisposeAsync()
		{
			Client.Dispose();
			await app.DisposeAsync();
		}
	}

	private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue("X-Test-User", out var userId))
				return Task.FromResult(AuthenticateResult.NoResult());
			var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], Scheme.Name));
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
		}
	}
}
