using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories;
using ZPI4M.Banking.Services.Connections;
using ZPI4M.Banking.Tink;
using ZPI4M.Banking.Tink.Http;
using ZPI4M.Database;
using ZPI4M.User.Database;
using ZPI4M.User.Services.BankingProfile;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
	public PostgresFactAttribute()
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")) &&
			!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
			Skip = "Set TEST_POSTGRES_CONNECTION_STRING to a disposable PostgreSQL server; tests create and drop their own uniquely named database.";
	}
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
	public PostgresTheoryAttribute()
	{
		Skip = new PostgresFactAttribute().Skip;
	}
}

public abstract class PostgresFixture : IAsyncLifetime
{
	private readonly string _databaseName = "banking_tests_" + Guid.NewGuid().ToString("N");
	protected string ConnectionString { get; private set; } = "";
	protected FakeTink Tink { get; } = new();
	protected MutableClock Clock { get; } = new();

	public async Task InitializeAsync()
	{
		var maintenance = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")!;
		if (string.IsNullOrWhiteSpace(maintenance))
			throw new InvalidOperationException("TEST_POSTGRES_CONNECTION_STRING is required in CI. Integration tests must not silently skip.");
		await using var admin = new NpgsqlConnection(maintenance);
		await admin.OpenAsync();
		await using (var command = new NpgsqlCommand($"CREATE DATABASE {_databaseName}", admin))
			await command.ExecuteNonQueryAsync();
		var builder = new NpgsqlConnectionStringBuilder(maintenance) { Database = _databaseName, Pooling = false };
		ConnectionString = builder.ConnectionString;
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		foreach (var file in new[] { "01-schemas.sql", "02-tables.sql" })
		{
			await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baseline", file)), connection);
			await command.ExecuteNonQueryAsync();
		}
		await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await context.Database.MigrateAsync();
		Assert.False(context.Database.HasPendingModelChanges());
	}

	public async Task DisposeAsync()
	{
		var maintenance = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING");
		if (string.IsNullOrWhiteSpace(maintenance))
			return;
		// Only this fixture's generated database is ever dropped, never the configured database.
		await using var connection = new NpgsqlConnection(maintenance);
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)", connection);
		await command.ExecuteNonQueryAsync();
	}

	protected async Task<Guid> AddUserAsync(bool active = true, string? tinkUserId = null)
	{
		var id = Guid.NewGuid();
		await using var context = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		context.Users.Add(new ZPI4M.User.Models.User
		{
			Id = id,
			Login = "test-" + id.ToString("N"),
			Name = "Test",
			Surname = "User",
			PasswordHash = "unused-test-value",
			Email = $"test-{id:N}@example.test",
			BirthDate = new DateOnly(2000, 1, 1),
			CreatedAt = DateTime.UtcNow,
			IsActive = active,
			TinkUserId = tinkUserId
		});
		await context.SaveChangesAsync();
		return id;
	}

	protected Session OpenSession() => new(ConnectionString, Tink, Clock);

	protected sealed class Session : IDisposable
	{
		public BankingDbContext Database { get; }
		private readonly UserDbContext _users;
		public BankingConnectionService Service { get; }
		public Session(string connectionString, ITinkConnectionGateway tink, TimeProvider clock)
		{
			Database = new(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(connectionString).Options);
			_users = new(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(connectionString).Options);
			var repository = new BankingConnectionRepository(Database, clock);
			Service = new(repository, repository, new UserBankingProfileService(_users), tink, clock,
			 Options.Create(new BankingConnectionOptions()), NullLogger<BankingConnectionService>.Instance);
		}
		public void Dispose() { Database.Dispose(); _users.Dispose(); }
	}

	protected sealed class MutableClock : TimeProvider
	{
		private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
		public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
		public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
	}

	protected sealed class FakeTink : ITinkConnectionGateway
	{
		private int _createCalls;
		public int CreateCalls => _createCalls;
		public bool FailLink { get; set; }
		public bool FailVerification { get; set; }
		public bool RejectCredentials { get; set; }
		public Func<Task>? BeforeVerify { get; set; }
		public Func<Task>? BeforeCreate { get; set; }
		public IReadOnlyList<TinkProviderConsent> Consents { get; set; } = [];
		public int ConsentCalls { get; private set; }
		public bool FailConsents { get; set; }
		public Task<IReadOnlyList<TinkProviderConsent>> GetConsentsAsync(string tinkUserId, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ConsentCalls++;
			return FailConsents ? throw new TinkApiException("provider_consents", "upstream_error") : Task.FromResult(Consents);
		}
		public async Task<string> CreateOrRecoverUserAsync(Guid userId, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _createCalls);
			if (BeforeCreate is not null)
				await BeforeCreate();
			await Task.Delay(25, cancellationToken);
			return "tink-" + userId.ToString("N");
		}
		public Task<string> CreateLinkAsync(string tinkUserId, string idHint, string state, CancellationToken cancellationToken)
		 => FailLink ? throw new TinkApiException("delegated_authorization", "timeout")
		  : Task.FromResult("https://example.test/link?state=" + state);
		public async Task<TinkVerifiedConnection?> VerifyConnectionAsync(string tinkUserId, string credentialsId, CancellationToken cancellationToken)
		{
			if (BeforeVerify is not null)
				await BeforeVerify();
			if (FailVerification)
				throw new TinkApiException("verify_connection", "timeout");
			return RejectCredentials ? null : new(credentialsId, "demo-provider");
		}
	}
}
