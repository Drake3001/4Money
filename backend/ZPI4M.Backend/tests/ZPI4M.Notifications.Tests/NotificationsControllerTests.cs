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
using ZPI4M.Notifications.ApiControllers;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Notifications.Tests;

public sealed class NotificationsControllerTests
{
	[Theory]
	[InlineData("invalid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public async Task InvalidAuthenticatedUserIdReturnsUnauthorizedWithoutCallingService(string userId)
	{
		await using var host = await TestHost.StartAsync();
		host.Client.DefaultRequestHeaders.Add("X-Test-User", userId);
		foreach (var (method, route) in new[]
		{
			("GET", "/api/notifications"),
			("PUT", "/api/notifications/read-all"),
			("PUT", "/api/notifications/90000000-0000-0000-0000-000000000001/read")
		})
		{
			using var response = await host.Client.SendAsync(new(new HttpMethod(method), route));
			Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
			Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		}
		Assert.Equal(0, host.Service.Calls);
	}

	[Theory]
	[InlineData("GET", "/api/notifications")]
	[InlineData("PUT", "/api/notifications/read-all")]
	[InlineData("PUT", "/api/notifications/90000000-0000-0000-0000-000000000001/read")]
	public async Task AnonymousRequestsAreRejectedBeforeCallingService(string method, string route)
	{
		await using var host = await TestHost.StartAsync();
		using var response = await host.Client.SendAsync(new(new HttpMethod(method), route));
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		Assert.Equal(0, host.Service.Calls);
	}

	[Theory]
	[InlineData("pageNumber=0")]
	[InlineData("pageNumber=-1")]
	[InlineData("pageSize=0")]
	[InlineData("pageSize=101")]
	[InlineData("pageNumber=2147483647&pageSize=100")]
	[InlineData("pageNumber=invalid")]
	[InlineData("isRead=invalid")]
	public async Task InvalidQueryReturnsValidationProblemBeforeCallingService(string query)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		using var response = await host.Client.GetAsync("/api/notifications?" + query);
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(400, problem.GetProperty("status").GetInt32());
		Assert.True(problem.GetProperty("errors").EnumerateObject().Any());
		Assert.Equal(0, host.Service.Calls);
	}

	[Theory]
	[InlineData("", 1, 20, null)]
	[InlineData("&pageNumber=2&pageSize=5&isRead=false", 2, 5, false)]
	[InlineData("&isRead=true", 1, 20, true)]
	public async Task InboxUsesJwtOwnerAndReturnsOnlyResponseFields(string query, int pageNumber, int pageSize, bool? isRead)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		using var response = await host.Client.GetAsync($"/api/notifications?userId={Guid.NewGuid()}{query}");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(response.Headers.CacheControl?.NoStore);
		Assert.Equal(host.UserId, host.Service.LastUserId);
		Assert.Equal((pageNumber, pageSize, isRead), host.Service.LastQuery);
		var json = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(pageNumber, json.GetProperty("pageNumber").GetInt32());
		Assert.Equal(pageSize, json.GetProperty("pageSize").GetInt32());
		Assert.Equal(41, json.GetProperty("totalCount").GetInt32());
		var item = Assert.Single(json.GetProperty("items").EnumerateArray());
		Assert.Equal(new[] { "id", "title", "text", "type", "createdAt", "groupId", "expenseId", "readAt", "isRead" },
			item.EnumerateObject().Select(p => p.Name));
		Assert.False(item.GetProperty("isRead").GetBoolean());
		Assert.Equal(1, host.Service.Calls);
	}

	[Theory]
	[InlineData(false, 404)]
	[InlineData(true, 204)]
	public async Task MarkReadUsesOwnerAndMapsMissingNotificationToProblem(bool found, int status)
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		host.Service.Found = found;
		var id = Guid.NewGuid();
		using var response = await host.Client.PutAsync($"/api/notifications/{id}/read?userId={Guid.NewGuid()}", null);
		Assert.Equal(status, (int)response.StatusCode);
		Assert.Equal(host.UserId, host.Service.LastUserId);
		Assert.Equal(id, host.Service.LastNotificationId);
		if (!found)
		{
			Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
			Assert.Equal(404, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
		}
	}

	[Fact]
	public async Task MarkAllUsesOwnerAndReturnsNoContentWithoutBody()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		using var response = await host.Client.PutAsync($"/api/notifications/read-all?userId={Guid.NewGuid()}", null);
		Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
		Assert.Equal(host.UserId, host.Service.LastUserId);
		Assert.Equal(1, host.Service.Calls);
	}

	[Fact]
	public async Task CreatingNotificationsIsNotExposedToClients()
	{
		await using var host = await TestHost.StartAsync();
		host.Authenticate();
		using var response = await host.Client.PostAsJsonAsync("/api/notifications", new { title = "Test" });
		Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
		Assert.Equal(0, host.Service.Calls);
	}

	private sealed class TestHost(WebApplication app, HttpClient client, FakeService service) : IAsyncDisposable
	{
		public Guid UserId { get; } = Guid.NewGuid();
		public HttpClient Client { get; } = client;
		public FakeService Service { get; } = service;
		public void Authenticate() => Client.DefaultRequestHeaders.Add("X-Test-User", UserId.ToString());
		public static async Task<TestHost> StartAsync()
		{
			var builder = WebApplication.CreateBuilder();
			builder.WebHost.UseTestServer();
			builder.Logging.ClearProviders();
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
			builder.Services.AddControllers().AddApplicationPart(typeof(NotificationsController).Assembly);
			builder.Services.AddProblemDetails();
			builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
			builder.Services.AddAuthorization();
			var service = new FakeService();
			builder.Services.AddSingleton<INotificationService>(service);
			var app = builder.Build();
			app.UseExceptionHandler();
			app.UseStatusCodePages();
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

	private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
		: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue("X-Test-User", out var userId))
				return Task.FromResult(AuthenticateResult.NoResult());
			var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], Scheme.Name));
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
		}
	}

	private sealed class FakeService : INotificationService
	{
		public int Calls { get; private set; }
		public Guid LastUserId { get; private set; }
		public Guid LastNotificationId { get; private set; }
		public (int, int, bool?) LastQuery { get; private set; }
		public bool Found { get; set; } = true;
		public Task<Guid?> NotifyAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("NotifyAsync must not be exposed over HTTP.");
		public Task<PagedResult<NotificationResponse>> GetByUserAsync(Guid userId, int pageNumber = 1, int pageSize = 20, bool? isRead = null, CancellationToken cancellationToken = default)
		{
			Calls++;
			LastUserId = userId;
			LastQuery = (pageNumber, pageSize, isRead);
			return Task.FromResult(new PagedResult<NotificationResponse>([
				new(Guid.NewGuid(), "Tytul", "Tresc", NotificationType.General, DateTime.UtcNow, null, null, null)
			], 41, pageNumber, pageSize));
		}
		public Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
		{
			Calls++;
			LastUserId = userId;
			LastNotificationId = notificationId;
			return Task.FromResult(Found);
		}
		public Task MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default)
		{
			Calls++;
			LastUserId = userId;
			return Task.CompletedTask;
		}
	}
}
