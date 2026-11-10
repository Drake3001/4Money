using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using ZPI4M.Database;

namespace ZPI4M.User.IntegrationTests;

public sealed class ActiveEmailUniquenessTests : PostgresFixture
{
	[PostgresFact]
	public async Task DatabaseAllowsInactiveDuplicatesButRejectsActiveDuplicatesAndReactivation()
	{
		await using var context = OpenDatabase();
		var duplicate = await Assert.ThrowsAsync<PostgresException>(() => context.Users.Where(u => u.Login == "seed_bartek")
			.ExecuteUpdateAsync(s => s.SetProperty(u => u.Email, "anna@example.test")));
		Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
		Assert.Equal("c_user_unique_email", duplicate.ConstraintName);

		await context.Users.Where(u => u.Login == "seed_bartek" || u.Login == "seed_celina")
			.ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false).SetProperty(u => u.Email, "anna@example.test"));
		Assert.Equal(3, await context.Users.CountAsync(u => u.Email == "anna@example.test"));
		Assert.Equal(1, await context.Users.CountAsync(u => u.Email == "anna@example.test" && u.IsActive));

		var reactivation = await Assert.ThrowsAsync<PostgresException>(() => context.Users.Where(u => u.Login == "seed_bartek")
			.ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true)));
		Assert.Equal(PostgresErrorCodes.UniqueViolation, reactivation.SqlState);
		Assert.False((await context.Users.SingleAsync(u => u.Login == "seed_bartek")).IsActive);
	}

	[PostgresFact]
	public async Task MigrationCanRoundTripFromGlobalIndexWithoutLosingUsers()
	{
		await using var context = OpenAppDatabase();
		var migrations = context.Database.GetMigrations().ToArray();
		var emailMigrationIndex = Array.FindIndex(migrations, m => m.EndsWith("_UserImprovements", StringComparison.Ordinal));
		var migrator = context.GetService<IMigrator>();
		await migrator.MigrateAsync(migrations[emailMigrationIndex - 1]);
		Assert.Null(await ReadEmailIndexFilterAsync());
		await migrator.MigrateAsync();
		Assert.Contains("is_active", await ReadEmailIndexFilterAsync());
		await using var users = OpenDatabase();
		Assert.Equal(5, await users.Users.CountAsync());
		Assert.False(context.Database.HasPendingModelChanges());
	}

	[PostgresFact]
	public async Task DowngradeWithReusedEmailFailsWithoutDeletingUsersOrPartialIndex()
	{
		await using (var users = OpenDatabase())
			await users.Users.Where(u => u.Login == "seed_dawid")
				.ExecuteUpdateAsync(s => s.SetProperty(u => u.Email, "anna@example.test"));
		await using var context = OpenAppDatabase();
		var migrations = context.Database.GetMigrations().ToArray();
		var emailMigrationIndex = Array.FindIndex(migrations, m => m.EndsWith("_UserImprovements", StringComparison.Ordinal));
		var error = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(migrations[emailMigrationIndex - 1]));
		Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
		Assert.Contains("is_active", await ReadEmailIndexFilterAsync());
		Assert.Contains(migrations[emailMigrationIndex], await context.Database.GetAppliedMigrationsAsync());
		// Later migrations may already have been reverted before the email downgrade failed.
		await context.GetService<IMigrator>().MigrateAsync();
		Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
		await using var database = OpenDatabase();
		Assert.Equal(5, await database.Users.CountAsync());
		Assert.Equal(2, await database.Users.CountAsync(u => u.Email == "anna@example.test"));
	}

	private AppDbContext OpenAppDatabase() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

	private async Task<string?> ReadEmailIndexFilterAsync()
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand("""
			SELECT pg_get_expr(i.indpred, i.indrelid)
			FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
			JOIN pg_namespace n ON n.oid = c.relnamespace
			WHERE n.nspname = 'user' AND c.relname = 'c_user_unique_email'
			""", connection);
		return await command.ExecuteScalarAsync() as string;
	}
}
