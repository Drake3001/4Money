using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using ZPI4M.Database;

namespace ZPI4M.Limits.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
	public PostgresFactAttribute()
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")) &&
			!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
			Skip = "Set TEST_POSTGRES_CONNECTION_STRING to a disposable PostgreSQL server; each test creates its own database.";
	}
}

public abstract class PostgresFixture : IAsyncLifetime
{
	protected static readonly Guid Admin = Guid.Parse("10000000-0000-0000-0000-000000000001");
	protected static readonly Guid Member = Guid.Parse("10000000-0000-0000-0000-000000000002");
	protected static readonly Guid Former = Guid.Parse("10000000-0000-0000-0000-000000000003");
	protected static readonly Guid GroupId = Guid.Parse("20000000-0000-0000-0000-000000000001");
	protected static readonly Guid OtherGroupId = Guid.Parse("20000000-0000-0000-0000-000000000002");
	protected static readonly Guid Category = Guid.Parse("30000000-0000-0000-0000-000000000099");
	private readonly string databaseName = "limits_tests_" + Guid.NewGuid().ToString("N");
	private string? maintenance;
	private bool created;
	protected string ConnectionString { get; private set; } = "";

	public async Task InitializeAsync()
	{
		maintenance = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING");
		if (string.IsNullOrWhiteSpace(maintenance)) throw new InvalidOperationException("TEST_POSTGRES_CONNECTION_STRING is required in CI.");
		await using (var admin = new NpgsqlConnection(maintenance))
		{
			await admin.OpenAsync();
			await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin);
			await command.ExecuteNonQueryAsync();
			created = true;
		}
		ConnectionString = new NpgsqlConnectionStringBuilder(maintenance) { Database = databaseName, Pooling = false, Timezone = "UTC" }.ConnectionString;
		foreach (var file in new[] { "01-schemas.sql", "02-tables.sql" })
			await ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baseline", file)));
		await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await db.Database.MigrateAsync();
		await ExecuteAsync("""
			INSERT INTO "user"."user" (_id, login, name, surname, password_hash, email, birth_date)
			SELECT ('10000000-0000-0000-0000-00000000000' || n)::uuid, 'limits' || n, 'Synthetic', 'User', 'unused',
			       'limits' || n || '@example.test', DATE '2000-01-01' FROM generate_series(1, 3) n;
			INSERT INTO grouping."group" (id, name, admin_user_id, currency_code) VALUES
			('20000000-0000-0000-0000-000000000001', 'Limits', '10000000-0000-0000-0000-000000000001', 'EUR'),
			('20000000-0000-0000-0000-000000000002', 'Other limits', '10000000-0000-0000-0000-000000000001', 'PLN');
			INSERT INTO grouping.group_user (user_id, group_id, joined_at, is_member)
			SELECT ('10000000-0000-0000-0000-00000000000' || n)::uuid, ('20000000-0000-0000-0000-00000000000' || g)::uuid,
			       CURRENT_TIMESTAMP, n != 3 FROM generate_series(1, 3) n CROSS JOIN generate_series(1, 2) g;
			INSERT INTO expense.category (_id, name, description, icon)
			VALUES ('30000000-0000-0000-0000-000000000099', 'Synthetic Limits', '', 'shopping-cart');
			""");
	}

	protected async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand(sql, connection);
		command.Parameters.AddRange(parameters);
		await command.ExecuteNonQueryAsync();
	}

	protected async Task<long> CountAsync()
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand("SELECT count(*) FROM limits.group_limit", connection);
		return (long)(await command.ExecuteScalarAsync())!;
	}

	public async Task DisposeAsync()
	{
		if (!created) return;
		await using var admin = new NpgsqlConnection(maintenance);
		await admin.OpenAsync();
		await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", admin);
		await command.ExecuteNonQueryAsync();
	}
}
