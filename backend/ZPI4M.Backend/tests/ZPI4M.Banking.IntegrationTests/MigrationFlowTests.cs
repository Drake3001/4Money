using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using ZPI4M.Database;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class MigrationFlowTests : PostgresFixture
{
	[PostgresFact]
	public async Task BankingMigrationCanBeRevertedAndReappliedAfterUserMigrations()
	{
		await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
			.UseNpgsql(ConnectionString).Options);
		var migrations = context.Database.GetMigrations().ToArray();
		Assert.EndsWith("_AddBanking", migrations[^1]);
		Assert.EndsWith("_AddAccountDeactivationTokens", migrations[^2]);
		var migrator = context.GetService<IMigrator>();

		await migrator.MigrateAsync(migrations[^2]);

		string[] newTables = ["bank_connection", "bank_connection_attempt", "bank_refresh", "bank_refresh_account"];
		foreach (var table in newTables)
		{
			Assert.True(await context.Database.SqlQuery<bool>(
				$"SELECT to_regclass({"banking." + table}) IS NULL AS \"Value\"").SingleAsync());
		}

		foreach (var table in new[] { "banking.bank_account", "banking.bank_transaction", "user.user_token" })
		{
			Assert.True(await context.Database.SqlQuery<bool>(
				$"SELECT to_regclass({table}) IS NOT NULL AS \"Value\"").SingleAsync());
		}

		await migrator.MigrateAsync();
		await migrator.MigrateAsync();

		Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
		Assert.False(context.Database.HasPendingModelChanges());
		foreach (var table in newTables)
		{
			Assert.True(await context.Database.SqlQuery<bool>(
				$"SELECT to_regclass({"banking." + table}) IS NOT NULL AS \"Value\"").SingleAsync());
		}
	}
}
