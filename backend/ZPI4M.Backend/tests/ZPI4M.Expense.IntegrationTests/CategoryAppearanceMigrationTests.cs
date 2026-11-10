using Xunit;

namespace ZPI4M.Expense.IntegrationTests;

public sealed class CategoryAppearanceMigrationTests : PostgresFixture
{
	[PostgresFact]
	public async Task SquashedMigrationRemovesColorAndSeedsLucideIconsAcrossRoundTrip()
	{
		await MigrateAsync();
		Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'expense' AND table_name = 'category' AND column_name = 'color'"));
		Assert.Equal("text", await ScalarAsync<string>("SELECT data_type FROM information_schema.columns WHERE table_schema = 'expense' AND table_name = 'category' AND column_name = 'icon'"));
		Assert.Equal("circle-help", await ScalarAsync<string>("SELECT icon FROM expense.category WHERE name = 'Nieznane'"));
		await MigrateAsync(PreviousMigration);
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'expense' AND table_name = 'category' AND column_name = 'color'"));
		await MigrateAsync();
		Assert.Equal("circle-help", await ScalarAsync<string>("SELECT icon FROM expense.category WHERE name = 'Nieznane'"));
	}
}
