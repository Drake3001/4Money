using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Notifications.ApiControllers;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.User.Database;
using ZPI4M.User.Models;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Repositories.UserTokenRepository;
using ZPI4M.User.Services.Authentication;
using ZPI4M.User.Services.Lookup;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.Notifications.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
	public PostgresFactAttribute()
	{
		if (string.IsNullOrWhiteSpace(PostgresFixture.TestServer) &&
			!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
			Skip = "Set TEST_POSTGRES_CONNECTION_STRING to a disposable PostgreSQL server.";
	}
}

public abstract class PostgresFixture : IAsyncLifetime
{
	internal static string? TestServer => Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING");
	protected static readonly Guid AnnaId = Guid.Parse("10000000-0000-0000-0000-000000000001");
	protected static readonly Guid BartekId = Guid.Parse("10000000-0000-0000-0000-000000000002");
	protected static readonly Guid CelinaId = Guid.Parse("10000000-0000-0000-0000-000000000003");
	protected static readonly Guid InactiveId = Guid.Parse("10000000-0000-0000-0000-000000000004");
	protected static readonly Guid FirstNotificationId = Guid.Parse("90000000-0000-0000-0000-000000000001");
	protected static readonly Guid GeneralNotificationId = Guid.Parse("90000000-0000-0000-0000-000000000003");
	private const string SigningKey = "notifications-integration-signing-key-at-least-32-bytes";
	private readonly string _databaseName = "notifications_tests_" + Guid.NewGuid().ToString("N");
	private string? _maintenance;
	private bool _created;
	protected string ConnectionString { get; private set; } = "";

	public async Task InitializeAsync()
	{
		_maintenance = TestServer;
		if (string.IsNullOrWhiteSpace(_maintenance))
			throw new InvalidOperationException("A disposable PostgreSQL server is required in CI; integration tests must not silently skip.");
		await using var admin = new NpgsqlConnection(_maintenance);
		await admin.OpenAsync();
		await using (var command = new NpgsqlCommand($"CREATE DATABASE {_databaseName}", admin))
			await command.ExecuteNonQueryAsync();
		_created = true;
		ConnectionString = new NpgsqlConnectionStringBuilder(_maintenance) { Database = _databaseName, Pooling = false, Timezone = "UTC" }.ConnectionString;
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		foreach (var file in new[] { "01-schemas.sql", "02-tables.sql" })
			await ExecuteFileAsync(connection, file);
		var legacyId = Guid.NewGuid();
		await using (var legacy = new NpgsqlCommand("""
			INSERT INTO notifications.notification (_id, text, sent_at)
			VALUES (@id, 'Legacy notification', TIMESTAMP '2026-01-01 12:00:00');
			""", connection))
		{
			legacy.Parameters.AddWithValue("id", legacyId);
			await legacy.ExecuteNonQueryAsync();
		}
		await using (var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options))
		{
			await context.Database.MigrateAsync();
			Assert.Empty(await context.Database.GetPendingMigrationsAsync());
			Assert.False(context.Database.HasPendingModelChanges());
		}
		await using (var migrated = OpenDatabase())
		{
			var legacy = await migrated.Notifications.SingleAsync(n => n.Id == legacyId);
			Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), legacy.CreatedAt);
			Assert.Equal("Legacy notification", legacy.Text);
			Assert.Equal(ZPI4M.Notifications.Models.NotificationType.General, legacy.Type);
			Assert.NotNull(legacy.Title);
			Assert.False(await migrated.NotificationRecipients.AnyAsync(r => r.FkNotificationId == legacyId));
		}
		await ExecuteFileAsync(connection, "seed_local.sql");
	}

	private static async Task ExecuteFileAsync(NpgsqlConnection connection, string file)
	{
		var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baseline", file));
		await using var command = new NpgsqlCommand(sql, connection);
		await command.ExecuteNonQueryAsync();
	}

	public async Task DisposeAsync()
	{
		if (!_created)
			return;
		await using var admin = new NpgsqlConnection(_maintenance);
		await admin.OpenAsync();
		// Only the generated database is dropped, never the database from the supplied connection string.
		await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)", admin);
		await command.ExecuteNonQueryAsync();
	}

	protected NotificationsDbContext OpenDatabase() => new(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(ConnectionString).Options);

	protected async Task<Guid> NotifyAsync(CreateNotificationRequest request)
	{
		var id = await TryNotifyAsync(request);
		Assert.NotNull(id);
		return id.Value;
	}

	protected async Task<Guid?> TryNotifyAsync(CreateNotificationRequest request)
	{
		await using var db = OpenDatabase();
		await using var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		var service = new NotificationService(new NotificationRepository(db), new NotificationRecipientRepository(db), db, new UserLookupService(users));
		return await service.NotifyAsync(request);
	}

	protected async Task<TestHost> StartHostAsync()
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["UserAuthentication:SigningKey"] = SigningKey });
		builder.Services.AddHttpContextAccessor();
		builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
		builder.Services.AddControllers().AddApplicationPart(typeof(NotificationsController).Assembly);
		builder.Services.AddProblemDetails();
		builder.Services.AddUserAuthentication(builder.Configuration);
		builder.Services.AddDbContext<UserDbContext>(o => o.UseNpgsql(ConnectionString));
		builder.Services.AddScoped<IUserRepository, UserRepository>();
		builder.Services.AddScoped<IUserLookupService, UserLookupService>();
		builder.Services.AddScoped<IUserTokenRepository, UserTokenRepository>();
		builder.Services.AddScoped<IPasswordHasher<ZPI4M.User.Models.User>, PasswordHasher<ZPI4M.User.Models.User>>();
		builder.Services.AddSingleton<IUserTokenGenerator, UserTokenGenerator>();
		builder.Services.AddDbContext<NotificationsDbContext>(o => o.UseNpgsql(ConnectionString));
		builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
		builder.Services.AddScoped<INotificationRecipientRepository, NotificationRecipientRepository>();
		builder.Services.AddScoped<INotificationService, NotificationService>();
		var app = builder.Build();
		app.UseExceptionHandler();
		app.UseStatusCodePages();
		app.UseAuthentication();
		app.UseAuthorization();
		app.MapControllers();
		await app.StartAsync();
		return new(app, app.GetTestClient());
	}

	protected async Task<Guid> AuthenticateAsync(TestHost host, Guid userId)
	{
		await using var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		var now = DateTime.UtcNow;
		var session = new UserToken
		{
			Id = Guid.NewGuid(),
			FkUserId = userId,
			Purpose = UserTokenPurpose.RefreshToken,
			TokenHash = Guid.NewGuid().ToString("N"),
			CreatedAt = now,
			ExpiresAt = now.AddHours(1)
		};
		users.UserTokens.Add(session);
		await users.SaveChangesAsync();
		var generator = new AccessTokenGenerator(Options.Create(new AuthenticationOptions { SigningKey = SigningKey }));
		host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", generator.Generate(userId, session.Id, now, session.ExpiresAt).Token);
		return session.Id;
	}

	protected sealed class TestHost(WebApplication app, HttpClient client) : IAsyncDisposable
	{
		public HttpClient Client { get; } = client;
		public async ValueTask DisposeAsync()
		{
			Client.Dispose();
			await app.DisposeAsync();
		}
	}
}
