using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;
using ZPI4M.Database;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class BankTransactionAssignmentTests : PostgresFixture
{
	private BankingDbContext Banking() => new(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(ConnectionString).Options);
	private AppDbContext Model() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
	private Task SeedAsync() => SeedScript.ExecuteAsync(ConnectionString,
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));

	[PostgresFact]
	public async Task MigrationCanBeRevertedAndReappliedWithoutChangingTransactions()
	{
		await using var db = Model();
		var migrations = db.Database.GetMigrations().ToArray();
		var assignmentMigration = Array.FindIndex(migrations, m => m.EndsWith("_AddBankTransactionAssignments", StringComparison.Ordinal));
		Assert.True(assignmentMigration > 0);
		await SeedAsync();
		await using var bank = Banking();
		var transactions = await bank.BankTransactions.OrderBy(t => t.Id).Select(t => t.Id).ToArrayAsync();
		var migrator = db.GetService<IMigrator>();
		await migrator.MigrateAsync(migrations[assignmentMigration - 1]);
		Assert.True(await db.Database.SqlQueryRaw<bool>(
			"SELECT to_regclass('banking.bank_transaction_assignment') IS NULL AS \"Value\"").SingleAsync());
		await migrator.MigrateAsync();
		Assert.False(db.Database.HasPendingModelChanges());
		Assert.Empty(await bank.BankTransactionAssignments.ToArrayAsync());
		Assert.Equal(transactions, await bank.BankTransactions.OrderBy(t => t.Id).Select(t => t.Id).ToArrayAsync());
	}

	[PostgresFact]
	public async Task SeedCanBeRepeatedAndRegistersBothConsumerTypesWithFreeTransactionsRemaining()
	{
		await SeedAsync();
		await SeedAsync();
		await using var bank = Banking();
		var actual = await bank.BankTransactionAssignments.AsNoTracking().OrderBy(a => a.BankTransactionId).ToArrayAsync();
		Assert.Equal(3, actual.Length);
		await using var model = Model();
		var expected = await model.Database.SqlQueryRaw<AssignmentRow>("""
			SELECT fk_bank_transaction_id AS "BankTransactionId", 1 AS "ConsumerType", _id AS "ConsumerId"
			FROM expense.expense WHERE fk_bank_transaction_id IS NOT NULL
			UNION ALL
			SELECT bank_transaction_id, 2, id FROM grouping.group_settlements WHERE bank_transaction_id IS NOT NULL
			""").ToArrayAsync();
		Assert.Equal(expected.OrderBy(a => a.BankTransactionId).Select(a => (a.BankTransactionId, a.ConsumerType, a.ConsumerId)),
			actual.Select(a => (a.BankTransactionId, (int)a.ConsumerType, a.ConsumerId)));
		Assert.Contains(actual, a => a.ConsumerType == BankTransactionConsumerType.Expense);
		Assert.Contains(actual, a => a.ConsumerType == BankTransactionConsumerType.GroupSettlement);
		Assert.True(await bank.BankTransactions.AnyAsync(t => !bank.BankTransactionAssignments.Any(a => a.BankTransactionId == t.Id)));
	}

	[PostgresTheory]
	[InlineData("INSERT INTO banking.bank_transaction_assignment SELECT bank_transaction_id, 2, 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid FROM banking.bank_transaction_assignment WHERE consumer_type = 1 LIMIT 1", "23505")]
	[InlineData("UPDATE banking.bank_transaction_assignment SET consumer_type = 3", "23514")]
	[InlineData("UPDATE banking.bank_transaction_assignment SET consumer_type = NULL", "23502")]
	[InlineData("UPDATE banking.bank_transaction_assignment SET consumer_id = NULL", "23502")]
	[InlineData("INSERT INTO banking.bank_transaction_assignment VALUES ('ffffffff-ffff-ffff-ffff-ffffffffffff', 1, 'ffffffff-ffff-ffff-ffff-ffffffffffff')", "23503")]
	[InlineData("DELETE FROM banking.bank_transaction WHERE _id = '50000000-0000-0000-0000-000000000003'", "23503")]
	public async Task DatabaseRejectsInvalidAssignments(string sql, string sqlState)
	{
		await SeedAsync();
		// Use a free transaction so only the assignment FK can prevent its deletion.
		if (sql.StartsWith("DELETE", StringComparison.Ordinal))
			await SeedScript.ExecuteAsync(ConnectionString, "INSERT INTO banking.bank_transaction_assignment VALUES ('50000000-0000-0000-0000-000000000003', 1, 'ffffffff-ffff-ffff-ffff-ffffffffffff')");
		var error = await Assert.ThrowsAsync<PostgresException>(() => SeedScript.ExecuteAsync(ConnectionString, sql));
		Assert.Equal(sqlState, error.SqlState);
	}

	private sealed class AssignmentRow
	{
		public Guid BankTransactionId { get; set; }
		public int ConsumerType { get; set; }
		public Guid ConsumerId { get; set; }
	}
}
