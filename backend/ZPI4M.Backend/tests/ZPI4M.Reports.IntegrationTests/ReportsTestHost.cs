using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Reports.ApiControllers;
using ZPI4M.Reports.Services.Reports;
using ZPI4M.Reports.TestSupport;
using ZPI4M.Shared.CurrentUser;

namespace ZPI4M.Reports.IntegrationTests;

internal sealed class ReportsTestHost(WebApplication app, HttpClient client) : IAsyncDisposable
{
	public HttpClient Client { get; } = client;

	public static async Task<ReportsTestHost> StartAsync(string connectionString, Guid actor)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		var mvc = builder.Services.AddControllers();
		mvc.PartManager.ApplicationParts.Clear();
		mvc.AddApplicationPart(typeof(ReportsController).Assembly);
		builder.Services.AddProblemDetails();
		builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
		builder.Services.AddAuthorization();
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
		builder.Services.AddDbContext<ExpenseDbContext>(options => options.UseNpgsql(connectionString));
		builder.Services.AddDbContext<GroupingDbContext>(options => options.UseNpgsql(connectionString));
		builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
		builder.Services.AddScoped<IGroupRepository, GroupRepository>();
		builder.Services.AddScoped<IGroupExpenseMetadataLookup, GroupExpenseMetadataLookup>();
		builder.Services.AddScoped<IExpenseAggregationService, ExpenseAggregationService>();
		builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
		// Metadata-only adapter keeps this host independent of write/OCR/storage dependencies.
		// Report aggregation, membership checks and category reads all use real PostgreSQL repositories.
		builder.Services.AddScoped<IExpenseService>(provider => CategoryMetadataProxy.Create(async token =>
			ExpenseResult<IReadOnlyList<CategoryDetails>>.Success(
				(await provider.GetRequiredService<ICategoryRepository>().GetAllAsync(token))
				.Select(category => new CategoryDetails(category.Id, category.Name, category.Description,
					category.Icon, category.Id == ExpenseSystemCategories.UnknownId)).ToArray())));
		builder.Services.AddScoped<IReportsService, ReportsService>();
		var app = builder.Build();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers();
		await app.StartAsync();
		var client = app.GetTestClient();
		client.DefaultRequestHeaders.Add("X-Test-User", actor.ToString());
		return new(app, client);
	}

	public async ValueTask DisposeAsync()
	{
		Client.Dispose();
		await app.DisposeAsync();
	}

	// Test authentication does not replace report aggregation or data-access authorization.
	private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue("X-Test-User", out var userId)) return Task.FromResult(AuthenticateResult.NoResult());
			var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], Scheme.Name));
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
		}
	}
}
