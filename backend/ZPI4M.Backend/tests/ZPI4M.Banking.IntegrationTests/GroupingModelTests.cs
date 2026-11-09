using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Repositories.ExpenseShareRepository;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class GroupingModelTests : PostgresFixture
{
	private const string GroupId = "20000000-0000-0000-0000-000000000001";
	private const string AnnaId = "10000000-0000-0000-0000-000000000001";
	private const string BartekId = "10000000-0000-0000-0000-000000000002";

	private AppDbContext OpenModel() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

	private async Task ExecuteAsync(string sql)
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand(sql, connection);
		await command.ExecuteNonQueryAsync();
	}

	private Task SeedAsync() => ExecuteAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));

	[PostgresFact]
	public async Task EmptyBaselineSupportsUpDownUpAndRejectsHistoricalDataBeforeDdl()
	{
		await using var db = OpenModel();
		var migrations = db.Database.GetMigrations().ToArray();
		var approved = Array.FindIndex(migrations, m => m.EndsWith("_AddGroupingModel", StringComparison.Ordinal));
		var migrator = db.GetService<IMigrator>();
		await migrator.MigrateAsync(migrations[approved - 1]);
		await ExecuteAsync($"INSERT INTO grouping.\"group\" (_id, name) VALUES ('{GroupId}', 'Legacy');");
		var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
		Assert.Contains("requires empty groups", error.MessageText);
		Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('grouping.group_member') IS NOT NULL AS \"Value\"").SingleAsync());
		Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('grouping.group_settlements') IS NULL AS \"Value\"").SingleAsync());
		Assert.Equal(migrations[..approved], await db.Database.GetAppliedMigrationsAsync());
		await ExecuteAsync("DELETE FROM grouping.\"group\";");
		await migrator.MigrateAsync();
		await migrator.MigrateAsync(migrations[approved - 1]);
		await migrator.MigrateAsync();
		Assert.False(db.Database.HasPendingModelChanges());
		await SeedAsync();
		await SeedAsync();
		await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(migrations[approved - 1]));
		// Newer migrations can be reverted before the Expense migration rejects historical rows.
		var temporary = Array.FindIndex(migrations, m => m.EndsWith("_AddTemporaryExpenseAggregationSupport", StringComparison.Ordinal));
		Assert.Equal(migrations[..(temporary + 1)], await db.Database.GetAppliedMigrationsAsync());
		await migrator.MigrateAsync();
		Assert.Equal(migrations, await db.Database.GetAppliedMigrationsAsync());
	}

	[PostgresTheory]
	[InlineData("UPDATE grouping.\"group\" SET currency_code = 'pln'", "23514")]
	[InlineData("UPDATE grouping.\"group\" SET currency_code = 'PL'", "23514")]
	[InlineData("UPDATE grouping.\"group\" SET currency_code = NULL", "23502")]
	[InlineData("UPDATE grouping.\"group\" SET admin_user_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'", "23503")]
	[InlineData("UPDATE grouping.group_user SET is_member = NULL", "23502")]
	[InlineData("UPDATE grouping.group_user SET total_paid = -0.01", "23514")]
	[InlineData("UPDATE grouping.group_user SET total_share = -0.01", "23514")]
	[InlineData("UPDATE grouping.group_user SET total_settlements_sent = -0.01", "23514")]
	[InlineData("UPDATE grouping.group_user SET total_settlements_received = -0.01", "23514")]
	[InlineData("UPDATE expense.expense_share SET amount = -0.01", "23514")]
	[InlineData("UPDATE expense.expense SET fk_payer_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'", "23503")]
	[InlineData("UPDATE grouping.group_settlements SET amount = 0", "23514")]
	[InlineData("UPDATE grouping.group_settlements SET from_user_id = to_user_id", "23514")]
	[InlineData("UPDATE grouping.group_settlements SET method = 4", "23514")]
	[InlineData("UPDATE grouping.group_settlements SET bank_transaction_id = '50000000-0000-0000-0000-000000000005'", "23505")]
	[InlineData("UPDATE grouping.group_invitation SET max_uses = 0", "23514")]
	[InlineData("UPDATE grouping.group_invitation SET use_count = -1", "23514")]
	[InlineData("UPDATE grouping.group_invitation SET use_count = max_uses + 1", "23514")]
	[InlineData("UPDATE grouping.group_invitation SET expires_at = NULL", "23502")]
	[InlineData("INSERT INTO grouping.group_invitation (id, group_id, created_by_user_id, token_hash, max_uses, use_count, expires_at, is_active) SELECT 'b0000000-0000-0000-0000-000000000002', group_id, created_by_user_id, token_hash, max_uses, use_count, expires_at, is_active FROM grouping.group_invitation", "23505")]
	[InlineData("UPDATE limits.group_limit SET amount = 0", "23514")]
	[InlineData("UPDATE limits.group_limit SET period = 4", "23514")]
	[InlineData("UPDATE limits.group_limit SET period = 1", "23505")]
	[InlineData("DELETE FROM \"user\".\"user\" WHERE _id = '10000000-0000-0000-0000-000000000001'", "23503")]
	[InlineData("UPDATE grouping.group_settlements SET group_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'", "23503")]
	[InlineData("UPDATE grouping.group_settlements SET from_user_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'", "23503")]
	[InlineData("UPDATE grouping.group_settlements SET to_user_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'", "23503")]
	[InlineData("UPDATE grouping.group_settlements SET bank_transaction_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff' WHERE id = (SELECT id FROM grouping.group_settlements LIMIT 1)", "23503")]
	[InlineData("UPDATE grouping.group_invitation SET group_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'", "23503")]
	[InlineData("UPDATE grouping.group_invitation SET created_by_user_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'", "23503")]
	[InlineData("UPDATE limits.group_limit SET group_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'", "23503")]
	public async Task DatabaseRejectsInvalidRows(string sql, string sqlState)
	{
		await SeedAsync();
		var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));
		Assert.Equal(sqlState, error.SqlState);
	}

	[PostgresFact]
	public async Task SeedAggregatesSourcesDefaultsAndRepositoriesAgree()
	{
		await SeedAsync();
		await using var model = OpenModel();
		Assert.False(model.Database.HasPendingModelChanges());
		Assert.True(await model.Database.SqlQueryRaw<bool>("""
			SELECT NOT EXISTS (
			 SELECT 1 FROM grouping.group_user m WHERE
			 m.total_paid <> COALESCE((SELECT sum(amount) FROM expense.expense e WHERE e.fk_group_id=m.group_id AND e.fk_payer_id=m.user_id),0) OR
			 m.total_share <> COALESCE((SELECT sum(s.amount) FROM expense.expense_share s JOIN expense.expense e ON e._id=s.fk_expense_id WHERE e.fk_group_id=m.group_id AND s.fk_user_id=m.user_id),0) OR
			 m.total_settlements_sent <> COALESCE((SELECT sum(amount) FROM grouping.group_settlements s WHERE s.group_id=m.group_id AND s.from_user_id=m.user_id),0) OR
			 m.total_settlements_received <> COALESCE((SELECT sum(amount) FROM grouping.group_settlements s WHERE s.group_id=m.group_id AND s.to_user_id=m.user_id),0)
			) AS "Value"
			""").SingleAsync());
		Assert.True(await model.Database.SqlQueryRaw<bool>("""
			SELECT NOT EXISTS (SELECT 1 FROM expense.expense e WHERE e.amount <> (SELECT sum(s.amount) FROM expense.expense_share s WHERE s.fk_expense_id=e._id)) AS "Value"
			""").SingleAsync());
		Assert.True(await model.Database.SqlQueryRaw<bool>("""
			SELECT NOT EXISTS (
			 SELECT 1 FROM expense.expense e
			 JOIN grouping."group" g ON g.id=e.fk_group_id
			 LEFT JOIN banking.bank_transaction t ON t._id=e.fk_bank_transaction_id
			 LEFT JOIN banking.bank_account a ON a._id=t.fk_bank_account_id
			 WHERE NOT EXISTS (SELECT 1 FROM grouping.group_user m WHERE m.group_id=e.fk_group_id AND m.user_id=e.fk_payer_id)
			 OR (e.fk_bank_transaction_id IS NOT NULL AND (t.currency_code<>g.currency_code OR a.fk_user_id<>e.fk_payer_id))
			) AND NOT EXISTS (
			 SELECT 1 FROM grouping.group_settlements s JOIN grouping."group" g ON g.id=s.group_id
			 LEFT JOIN banking.bank_transaction t ON t._id=s.bank_transaction_id
			 LEFT JOIN banking.bank_account a ON a._id=t.fk_bank_account_id
			 WHERE (s.bank_transaction_id IS NOT NULL AND (t.currency_code<>g.currency_code OR a.fk_user_id<>s.from_user_id))
			 OR EXISTS (SELECT 1 FROM expense.expense e WHERE e.fk_bank_transaction_id=s.bank_transaction_id)
			) AS "Value"
			""").SingleAsync());
		await using var grouping = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		var groups = new GroupRepository(grouping);
		Assert.Single((await groups.GetPageAsync(new GroupQuery { UserId = Guid.Parse(AnnaId), IsAdmin = true })).Items);
		Assert.Single((await groups.GetPageAsync(new GroupQuery { UserId = Guid.Parse(BartekId), IsAdmin = false })).Items);
		var members = new GroupMemberRepository(grouping);
		Assert.True(await members.IsAdminAsync(Guid.Parse(GroupId), Guid.Parse(AnnaId)));
		Assert.False(await members.IsAdminAsync(Guid.Parse(GroupId), Guid.Parse(BartekId)));
		Assert.Equal(1, await members.CountAsync(Guid.Parse(GroupId), true));
		Assert.Single((await members.GetPageAsync(Guid.Parse(GroupId), new GroupMemberQuery { IsAdmin = false, SortBy = GroupMemberSortBy.IsAdmin })).Items);
		await using var expense = new ExpenseDbContext(new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(ConnectionString).Options);
		var shares = new ExpenseShareRepository(expense);
		Assert.Equal(1500m, await shares.GetTotalAmountAsync(Guid.Parse("70000000-0000-0000-0000-000000000004")));
		Assert.Equal(2, (await shares.GetPageAsync(new ExpenseShareQuery { MinAmount = 100m, SortBy = ExpenseShareSortBy.Amount })).Items.Count);
		Assert.Single((await new ExpenseRepository(expense).GetByGroupAsync(Guid.Parse(GroupId), new ExpenseQuery { Name = "Czynsz", DateFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)), DateTo = DateOnly.FromDateTime(DateTime.UtcNow), SortBy = ExpenseSortBy.TransactionTime })).Items);
		await ExecuteAsync($"INSERT INTO grouping.group_user (group_id, user_id, joined_at) VALUES ('{GroupId}', '10000000-0000-0000-0000-000000000003', CURRENT_TIMESTAMP);");
		var joined = await grouping.GroupMembers.SingleAsync(m => m.FkGroupId == Guid.Parse(GroupId) && m.FkUserId == Guid.Parse("10000000-0000-0000-0000-000000000003"));
		Assert.Equal(DateTimeKind.Utc, joined.JoinedAt.Kind);
		Assert.True(joined.IsMember);
		joined.IsMember = false;
		await grouping.SaveChangesAsync();
		grouping.ChangeTracker.Clear();
		Assert.False((await grouping.GroupMembers.SingleAsync(m => m.FkGroupId == joined.FkGroupId && m.FkUserId == joined.FkUserId)).IsMember);
		grouping.GroupMembers.Add(new ZPI4M.Grouping.Models.GroupMember
		{
			FkGroupId = Guid.Parse(GroupId),
			FkUserId = Guid.Parse("10000000-0000-0000-0000-000000000004"),
			JoinedAt = DateTime.UtcNow,
			IsMember = false
		});
		await grouping.SaveChangesAsync();
		grouping.ChangeTracker.Clear();
		Assert.False((await grouping.GroupMembers.SingleAsync(m => m.FkUserId == Guid.Parse("10000000-0000-0000-0000-000000000004"))).IsMember);
		Assert.Equal(0m, joined.TotalPaid + joined.TotalShare + joined.TotalSettlementsSent + joined.TotalSettlementsReceived);
		Assert.True((await grouping.GroupInvitations.SingleAsync()).IsActive);
		Assert.Equal(3, await grouping.GroupSettlements.CountAsync());
	}
}
