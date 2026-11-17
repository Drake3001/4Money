using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ZPI4M.Expense.IntegrationTests;

public sealed class ExpenseSystemCategoryMigrationTests : PostgresFixture
{
	[PostgresTheory]
	[InlineData("30000000-0000-0000-0000-000000000005", "Other")]
	[InlineData("30000000-0000-0000-0000-000000000099", "Nieznane")]
	public async Task MigrationRejectsConflictingSystemCategoryWithoutRecordingMigration(string id, string name)
	{
		await ExecuteAsync("INSERT INTO expense.category (_id, name, description, icon, color) VALUES (@id, @name, '', '', '')",
			new NpgsqlParameter("id", Guid.Parse(id)), new NpgsqlParameter("name", name));
		var error = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync());
		Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
		await using var context = OpenDatabase();
		Assert.DoesNotContain(await context.Database.GetAppliedMigrationsAsync(),
			migration => migration.EndsWith("_CompleteExpenseModel", StringComparison.Ordinal));
	}

	[PostgresFact]
	public async Task FreshSchemaSeedsAllDefaultCategoriesOnce()
	{
		await MigrateAsync();
		await MigrateAsync();
		Assert.Equal(21L, await ScalarAsync<long>("SELECT count(*) FROM expense.category"));
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM expense.category WHERE name = 'Nieznane'"));
		Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM expense.category WHERE left(name, 1) <> upper(left(name, 1))"));
		Assert.Equal("Kaucja", await ScalarAsync<string>("SELECT name FROM expense.category WHERE icon = 'recycle'"));
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM expense.category WHERE icon = 'recycle'"));
	}

	[PostgresFact]
	public async Task DowngradeRefusesToRemoveSystemCategoryInUse()
	{
		await SeedAsync();
		await ExecuteAsync("""
			INSERT INTO expense.expense_item (_id, fk_expense_id, fk_category_id, name, unit_price, quantity, is_system_generated)
			VALUES ('80000000-0000-0000-0000-000000000099', '70000000-0000-0000-0000-000000000001',
			        '30000000-0000-0000-0000-000000000005', 'Nieznane', 1, 1, true);
			""");
		var target = PreviousMigration;
		var error = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(target));
		Assert.Contains("requires empty expenses", error.MessageText);
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM expense.category WHERE name = 'Nieznane'"));
	}

	[PostgresFact]
	public async Task EmptyDatabaseCanDowngradeAndUpgradeAgain()
	{
		await MigrateAsync();
		await MigrateAsync(PreviousMigration);
		Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM expense.category WHERE name = 'Nieznane'"));
		await MigrateAsync();
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM expense.category WHERE name = 'Nieznane'"));
	}

}
