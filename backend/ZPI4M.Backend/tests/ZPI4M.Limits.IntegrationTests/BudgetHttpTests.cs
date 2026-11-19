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
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Categories;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Access;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Limits.ApiControllers;
using ZPI4M.Limits.Services;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Limits.IntegrationTests;

public sealed partial class LimitAlertTests
{
	[PostgresFact]
	public async Task HttpUsesRealRegistrationsAndRejectsRemovedFieldsAndOperations()
	{
		await PrepareAsync();
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Configuration["LimitsAlertsSweep:Enabled"] = "false";
		builder.Services.AddSingleton<TimeProvider>(new SweepClock("2026-11-14T12:00:00Z"));
		builder.Services.AddLimits();
		builder.Services.AddScoped(_ => Open());
		builder.Services.AddScoped(p => p.GetRequiredService<Scope>().Limits);
		builder.Services.AddScoped(p => p.GetRequiredService<Scope>().Expense);
		builder.Services.AddScoped(p => p.GetRequiredService<Scope>().Grouping);
		builder.Services.AddScoped<IGroupingTransactionCoordinator>(p => p.GetRequiredService<Scope>().Tx);
		builder.Services.AddScoped<IGroupRepository, GroupRepository>();
		builder.Services.AddScoped<IGroupMemberRepository, GroupMemberRepository>();
		builder.Services.AddScoped<IGroupAccessService, GroupAccessService>();
		builder.Services.AddScoped<IGroupMemberLookup, GroupMemberLookup>();
		builder.Services.AddScoped<IGroupExpenseMetadataLookup, GroupExpenseMetadataLookup>();
		builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
		builder.Services.AddScoped<ICategoryLookupService, CategoryLookupService>();
		builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
		builder.Services.AddSingleton<IExpenseCalculationService, ExpenseCalculationService>();
		builder.Services.AddScoped<IExpenseAggregationService, ExpenseAggregationService>();
		builder.Services.AddScoped<IExpenseSystemAggregationService, ExpenseSystemAggregationService>();
		builder.Services.AddScoped<IUserLookupService>(p => new UserLookupService(p.GetRequiredService<Scope>().UsersDb));
		builder.Services.AddScoped<INotificationService>(p =>
		{
			var s = p.GetRequiredService<Scope>();
			return new NotificationService(new NotificationRepository(s.Inbox), new NotificationRecipientRepository(s.Inbox), s.Inbox, new UserLookupService(s.UsersDb));
		});
		builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, BudgetAuth>("Test", _ =>
		{
		});
		builder.Services.AddAuthorization();
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
		var mvc = builder.Services.AddControllers();
		mvc.PartManager.ApplicationParts.Clear();
		mvc.AddApplicationPart(typeof(UserLimitsController).Assembly);
		builder.Services.AddProblemDetails();
		builder.Services.AddOpenApi();
		await using var app = builder.Build();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers();
		app.MapOpenApi();
		await app.StartAsync();
		using var client = app.GetTestClient();
		Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me/limits")).StatusCode);
		client.DefaultRequestHeaders.Add("X-Test-User", Admin.ToString());
		foreach (var collection in new[]
		{
			"/api/users/me/limits",
			$"/api/groups/{GroupId}/limits"}

		)
		{
			var body = new Dictionary<string, object>
			{
				{
					"amount",
					"100"
				},
				{
					"period",
					2
				}
			};
			if (collection.Contains("/users/", StringComparison.Ordinal))
				body["currencyCode"] = "PLN";
			using var created = await client.PostAsJsonAsync(collection, body);
			Assert.Equal(HttpStatusCode.Created, created.StatusCode);
			var row = await created.Content.ReadFromJsonAsync<JsonElement>();
			var id = row.GetProperty("id").GetGuid();
			var version = row.GetProperty("version").GetString();
			Assert.False(row.TryGetProperty("usage", out _));
			Assert.False(row.TryGetProperty("autoRenew", out _));
			Assert.Equal("2026-11-01", row.GetProperty("periodStartDate").GetString());
			using var details = await client.GetAsync($"{collection}/{id}");
			Assert.Equal(HttpStatusCode.OK, details.StatusCode);
			var usage = (await details.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("usage");
			Assert.False(usage.TryGetProperty("status", out _));
			Assert.Equal("PLN", usage.GetProperty("currencyCode").GetString());
			Assert.Equal("0.00", usage.GetProperty("spentAmount").GetString());
			Assert.Equal("100.00", usage.GetProperty("remainingAmount").GetString());
			Assert.Equal(0m, usage.GetProperty("usedPercentage").GetDecimal());
			Assert.False(usage.GetProperty("isExceeded").GetBoolean());
			var overlappingTripBody = new Dictionary<string, object>(body)
			{
				["period"] = 4,
				["startDate"] = "2026-11-13",
				["endDate"] = "2026-11-15"
			};
			using var firstTrip = await client.PostAsJsonAsync(collection, overlappingTripBody);
			using var secondTrip = await client.PostAsJsonAsync(collection, overlappingTripBody);
			Assert.Equal(HttpStatusCode.Created, firstTrip.StatusCode);
			Assert.Equal(HttpStatusCode.Created, secondTrip.StatusCode);
			using var stillEnabled = await client.GetAsync($"{collection}/{id}");
			var unchanged = await stillEnabled.Content.ReadFromJsonAsync<JsonElement>();
			Assert.True(unchanged.GetProperty("isEnabled").GetBoolean());
			Assert.Equal(version, unchanged.GetProperty("version").GetString());
			using var edit = await client.PutAsJsonAsync($"{collection}/{id}", new { amount = "200", version });
			Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
			using var stale = await client.PutAsJsonAsync($"{collection}/{id}", new { amount = "300", version });
			Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
			Assert.Equal("VersionConflict", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
			version = (await edit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetString();
			Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"{collection}/{id}/enabled", new { isEnabled = false, version })).StatusCode);
			Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync($"{collection}/{id}")).StatusCode);
			Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync($"{collection}/{id}/renewal", new { autoRenew = true })).StatusCode);
			foreach (var field in new[]
			{
				"startDate",
				"autoRenew",
				"previousLimitId",
				"endDate"
			}

			)
			{
				var obsolete = new Dictionary<string, object>(body)
				{
					{
						field,
						field == "autoRenew" ? (object)false : "2026-11-01"
					}
				};
				Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(collection, obsolete)).StatusCode);
			}

			Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{collection}/{id}", new { amount = "100", version, period = 1 })).StatusCode);
			var missingDatesBody = new Dictionary<string, object>(body) { ["period"] = 4 };
			using var missingDates = await client.PostAsJsonAsync(collection, missingDatesBody);
			Assert.Equal(HttpStatusCode.BadRequest, missingDates.StatusCode);
			var dateErrors = (await missingDates.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
			Assert.True(dateErrors.TryGetProperty("startDate", out _));
			Assert.True(dateErrors.TryGetProperty("endDate", out _));
			Assert.False(dateErrors.TryGetProperty("period", out _));
			var unknownPeriodBody = new Dictionary<string, object>(body) { ["period"] = 99 };
			Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(collection, unknownPeriodBody)).StatusCode);
			var customBody = new Dictionary<string, object>
			{
				{
					"amount",
					"100"
				},
				{
					"period",
					4
				},
				{
					"startDate",
					"2026-11-14"
				},
				{
					"endDate",
					"2026-12-15"
				}
			};
			if (collection.Contains("/users/", StringComparison.Ordinal))
				customBody["currencyCode"] = "PLN";
			using var trip = await client.PostAsJsonAsync(collection, customBody);
			Assert.Equal(HttpStatusCode.Created, trip.StatusCode);
			var tripRow = await trip.Content.ReadFromJsonAsync<JsonElement>();
			Assert.Equal("2026-11-14", tripRow.GetProperty("periodStartDate").GetString());
			Assert.Equal("2026-12-15", tripRow.GetProperty("periodEndDate").GetString());
			Assert.Equal(4, tripRow.GetProperty("period").GetInt32());
			var tripId = tripRow.GetProperty("id").GetGuid();
			var tripVersion = tripRow.GetProperty("version").GetString();
			Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{collection}/{tripId}", new { amount = "200", version = tripVersion, startDate = "2026-11-15" })).StatusCode);
		}

		using var openApi = await client.GetAsync("/openapi/v1.json");
		Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
		var document = await openApi.Content.ReadFromJsonAsync<JsonElement>();
		var schemas = document.GetProperty("components").GetProperty("schemas");
		foreach (var response in new[] { "UserLimitResponse", "GroupLimitResponse" })
			foreach (var date in new[] { "periodStartDate", "periodEndDate" })
			{
				var property = schemas.GetProperty(response).GetProperty("properties").GetProperty(date);
				Assert.Equal("string", property.GetProperty("type").GetString());
				Assert.Equal("date", property.GetProperty("format").GetString());
			}
		Assert.False(schemas.GetProperty("CreateUserLimitRequest").GetProperty("properties").TryGetProperty("autoRenew", out _));
		Assert.True(schemas.GetProperty("SetLimitEnabledRequest").GetProperty("properties").TryGetProperty("version", out _));
	}

	private sealed class BudgetAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue("X-Test-User", out var id))
				return Task.FromResult(AuthenticateResult.NoResult());
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], Scheme.Name)), Scheme.Name)));
		}
	}
}
