using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Xunit;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Models;
using ExpenseModel = ZPI4M.Expense.Models.Expense;

namespace ZPI4M.Expense.IntegrationTests;

public sealed class ExpenseRelationshipTests : PostgresFixture
{
	[PostgresFact]
	public async Task ModelsDescribeInternalShareRelationshipWithoutImportingOtherModulesIntoExpenseContext()
	{
		await MigrateAsync();
		await using var module = new ExpenseDbContext(new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(ConnectionString).Options);
		Assert.Equal(4, module.Model.GetEntityTypes().Count());
		Assert.All(module.Model.GetEntityTypes(), entity => Assert.Equal("ZPI4M.Expense.Models", entity.ClrType.Namespace));
		Assert.Empty(module.Model.FindEntityType(typeof(ExpenseModel))!.GetForeignKeys());
		Assert.Single(module.Model.FindEntityType(typeof(ExpenseShare))!.GetForeignKeys());
		AssertRelationship(module.Model, typeof(ExpenseShare), typeof(ExpenseModel), nameof(ExpenseShare.FkExpenseId),
			"c_expense_share_fk_expense_id", DeleteBehavior.NoAction, true);
		AssertItemRelationships(module.Model);

		await using var shared = OpenDatabase();
		Assert.Single(shared.Model.FindEntityType(typeof(ExpenseModel))!.GetForeignKeys());
		AssertRelationship(shared.Model, typeof(ExpenseModel), typeof(ZPI4M.User.Models.User), nameof(ExpenseModel.FkPayerId),
			"c_expense_fk_payer_id", DeleteBehavior.NoAction, true);
		Assert.Single(shared.Model.FindEntityType(typeof(ExpenseShare))!.GetForeignKeys());
		AssertRelationship(shared.Model, typeof(ExpenseShare), typeof(ExpenseModel), nameof(ExpenseShare.FkExpenseId),
			"c_expense_share_fk_expense_id", DeleteBehavior.NoAction, true);
		AssertRelationship(shared.Model, typeof(ZPI4M.Notifications.Models.Notification), typeof(ExpenseModel),
			nameof(ZPI4M.Notifications.Models.Notification.FkExpenseId), "c_notification_fk_expense_id", DeleteBehavior.SetNull, false);
		AssertItemRelationships(shared.Model);
		var expense = shared.Model.FindEntityType(typeof(ExpenseModel))!;
		Assert.True(Assert.Single(expense.GetIndexes(), index => index.GetDatabaseName() == "c_expense_unique_fk_bank_transaction_id").IsUnique);
		Assert.True(Assert.Single(expense.GetIndexes(), index => index.GetDatabaseName() == "c_expense_unique_fk_receipt_id").IsUnique);
		Assert.DoesNotContain(expense.GetIndexes(), index => index.GetDatabaseName() == "IX_expense_fk_group_id");
		Assert.DoesNotContain(shared.Model.FindEntityType(typeof(ExpenseShare))!.GetIndexes(), index => index.GetDatabaseName() == "IX_expense_share_fk_user_id");
		await AssertConstraintCountsAsync();
		Assert.Equal(0L, await ScalarAsync<long>("""
			SELECT count(*) FROM pg_indexes WHERE schemaname = 'expense'
			AND indexname IN ('IX_expense_fk_group_id', 'IX_expense_share_fk_user_id');
			"""));
	}

	[PostgresFact]
	public async Task SquashedMigrationPreservesBaselineForeignKeyObjectsAcrossRoundTrip()
	{
		var before = await ReadConstraintsAsync();
		await MigrateAsync();
		await MigrateAsync(PreviousMigration);
		Assert.Equal(before, await ReadConstraintsAsync());
		await MigrateAsync();
		await AssertConstraintCountsAsync();
	}

	private static void AssertItemRelationships(IModel model)
	{
		AssertRelationship(model, typeof(ExpenseItem), typeof(ExpenseModel), nameof(ExpenseItem.FkExpenseId),
			"c_expense_item_fk_expense_id", DeleteBehavior.ClientSetNull, true);
		AssertRelationship(model, typeof(ExpenseItem), typeof(Category), nameof(ExpenseItem.FkCategoryId),
			"c_expense_item_fk_category_id", DeleteBehavior.ClientSetNull, true);
	}

	private static void AssertRelationship(IModel model, Type dependent, Type principal, string property,
		string constraint, DeleteBehavior behavior, bool required)
	{
		var entity = model.FindEntityType(dependent)!;
		var relationship = Assert.Single(entity.GetForeignKeys(), foreignKey =>
			foreignKey.PrincipalEntityType.ClrType == principal && foreignKey.Properties.Select(member => member.Name).SequenceEqual([property]));
		Assert.Equal(constraint, relationship.GetConstraintName());
		Assert.Equal(behavior, relationship.DeleteBehavior);
		Assert.Equal(required, relationship.IsRequired);
		Assert.Equal(["Id"], relationship.PrincipalKey.Properties.Select(member => member.Name));
	}

	private async Task AssertConstraintCountsAsync()
	{
		Assert.Equal(8L, await ScalarAsync<long>("""
			SELECT count(*) FROM pg_constraint WHERE contype = 'f'
			AND conrelid IN ('expense.expense'::regclass, 'expense.expense_item'::regclass, 'expense.expense_share'::regclass)
			AND confdeltype = 'a';
			"""));
		Assert.Equal(8L, await ScalarAsync<long>("""
			SELECT count(*) FROM pg_constraint WHERE contype = 'f'
			AND conrelid IN ('expense.expense'::regclass, 'expense.expense_item'::regclass, 'expense.expense_share'::regclass);
			"""));
		Assert.Equal(1L, await ScalarAsync<long>("""
			SELECT count(*) FROM pg_constraint WHERE contype = 'f'
			AND conrelid = 'notifications.notification'::regclass
			AND conname = 'c_notification_fk_expense_id' AND confdeltype = 'n';
			"""));
	}

	private Task<string> ReadConstraintsAsync() => ScalarAsync<string>("""
		SELECT jsonb_object_agg(conname, jsonb_build_object('oid', oid::text, 'definition', pg_get_constraintdef(oid)) ORDER BY conname)::text
		FROM pg_constraint WHERE contype = 'f' AND conname IN (
		    'c_expense_fk_group_id', 'c_expense_fk_payer_id', 'c_expense_fk_bank_transaction_id', 'c_expense_fk_receipt_id',
		    'c_expense_share_fk_expense_id', 'c_expense_share_fk_user_id',
		    'c_expense_item_fk_expense_id', 'c_expense_item_fk_category_id', 'c_notification_fk_expense_id');
		""");

}
