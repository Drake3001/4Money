using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using ZPI4M.Database;

namespace ZPI4M.Expense.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
	public PostgresFactAttribute()
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")) &&
			!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
			Skip = "Set TEST_POSTGRES_CONNECTION_STRING to a disposable PostgreSQL server; each test creates its own database.";
	}
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
	public PostgresTheoryAttribute() => Skip = new PostgresFactAttribute().Skip;
}

public abstract class PostgresFixture : IAsyncLifetime
{
	protected const string PreviousMigration = "20261102215301_AddGroupingModel";
	protected static readonly Guid UserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
	protected static readonly Guid ExpenseId = Guid.Parse("70000000-0000-0000-0000-000000000001");
	private readonly string _databaseName = "expense_tests_" + Guid.NewGuid().ToString("N");
	private string? _maintenance;
	private bool _created;
	protected string ConnectionString { get; private set; } = "";

	public async Task InitializeAsync()
	{
		_maintenance = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING");
		if (string.IsNullOrWhiteSpace(_maintenance))
			throw new InvalidOperationException("TEST_POSTGRES_CONNECTION_STRING is required in CI.");
		await using var admin = new NpgsqlConnection(_maintenance);
		await admin.OpenAsync();
		await using (var command = new NpgsqlCommand($"CREATE DATABASE {_databaseName}", admin))
			await command.ExecuteNonQueryAsync();
		_created = true;
		ConnectionString = new NpgsqlConnectionStringBuilder(_maintenance) { Database = _databaseName, Pooling = false, Timezone = "UTC" }.ConnectionString;
		foreach (var file in new[] { "01-schemas.sql", "02-tables.sql" })
			await ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baseline", file)));
		await MigrateAsync(PreviousMigration);
	}

	protected AppDbContext OpenDatabase() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

	protected async Task MigrateAsync(string? target = null)
	{
		await using var context = OpenDatabase();
		await context.GetService<IMigrator>().MigrateAsync(target);
	}


	protected async Task SeedAsync()
	{
		await MigrateAsync();
		await ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
	}

	protected async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand(sql, connection);
		command.Parameters.AddRange(parameters);
		await command.ExecuteNonQueryAsync();
	}

	protected async Task<T> ScalarAsync<T>(string sql)
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand(sql, connection);
		return (T)(await command.ExecuteScalarAsync())!;
	}

	protected async Task AssertConstraintAsync(string sql, string state, string? constraint = null)
	{
		var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));
		Assert.Equal(state, error.SqlState);
		if (constraint is not null) Assert.Equal(constraint, error.ConstraintName);
	}

	protected Task InsertLegacyAsync() => ExecuteAsync("""
		INSERT INTO "user"."user" (_id, login, name, surname, password_hash, email, birth_date)
		VALUES ('10000000-0000-0000-0000-000000000001', 'legacy', 'Test', 'User', 'unused', 'legacy@example.test', DATE '2000-01-01');
		INSERT INTO grouping."group" (id, name, admin_user_id, currency_code) VALUES ('20000000-0000-0000-0000-000000000001', 'Legacy', '10000000-0000-0000-0000-000000000001', 'PLN');
		INSERT INTO expense.category (_id, name, description, icon, color)
		VALUES ('30000000-0000-0000-0000-000000000001', 'Legacy', '', '', '');
		INSERT INTO expense.expense (_id, fk_group_id, name, amount, transaction_time)
		VALUES ('70000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', 'Legacy purchase', 20, CURRENT_TIMESTAMP);
		INSERT INTO expense.expense_item (_id, fk_expense_id, fk_category_id, fk_user_id, name, unit_price, quantity)
		VALUES ('80000000-0000-0000-0000-000000000001', '70000000-0000-0000-0000-000000000001',
		'30000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'Legacy item', 10, 2);
		""");

	public virtual async Task DisposeAsync()
	{
		if (!_created) return;
		await using var admin = new NpgsqlConnection(_maintenance);
		await admin.OpenAsync();
		// Never drop the configured database: only this fixture's generated database.
		await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)", admin);
		await command.ExecuteNonQueryAsync();
	}
}
