using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Repositories.ExpenseItemRepository;

namespace ZPI4M.Expense.IntegrationTests;

public sealed class ExpenseMigrationTests : PostgresFixture
{
	[PostgresFact]
	public async Task FreshDatabaseMigratesAndSeedRemainsConsistent()
	{
		await SeedAsync();
		await using var context = OpenDatabase();
		Assert.Empty(await context.Database.GetPendingMigrationsAsync());
		Assert.False(context.Database.HasPendingModelChanges());
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense WHERE fk_payer_id IS NOT NULL"));
		Assert.Equal(7L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense_item WHERE discount = 0 AND unit IS NULL AND NOT is_system_generated"));
		Assert.Equal(0L, await ScalarAsync<long>("""
			SELECT count(*) FROM expense.expense e
			WHERE e.amount <> (SELECT sum(round(i.unit_price * i.quantity, 2) + i.discount)
			FROM expense.expense_item i WHERE i.fk_expense_id = e._id)
			"""));
		Assert.Equal(0L, await ScalarAsync<long>("""
			SELECT count(*) FROM expense.expense e
			WHERE 100 <> (SELECT sum(s.percentage) FROM expense.expense_share s WHERE s.fk_expense_id = e._id)
			"""));
	}

	[PostgresFact]
	public async Task SquashedUpgradeRejectsExistingExpensesWithoutChangingData()
	{
		await InsertLegacyAsync();
		var error = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync());
		Assert.Contains("requires empty expenses", error.MessageText);
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense_item WHERE fk_user_id IS NOT NULL"));
		await using var context = OpenDatabase();
		Assert.DoesNotContain(await context.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_CompleteExpenseModel", StringComparison.Ordinal));
	}

	[PostgresFact]
	public async Task PayerForeignKeyAndNotNullAreEnforced()
	{
		await SeedAsync();
		await AssertConstraintAsync("UPDATE expense.expense SET fk_payer_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'",
			PostgresErrorCodes.ForeignKeyViolation, "c_expense_fk_payer_id");
		await AssertConstraintAsync("UPDATE expense.expense SET fk_payer_id = NULL", PostgresErrorCodes.NotNullViolation);
	}

	[PostgresTheory]
	[InlineData(-5, 0)]
	[InlineData(0, 0)]
	[InlineData(10, -3)]
	public async Task ModuleContextPersistsSignedPricesDiscountAndMetadata(int price, int discount)
	{
		await SeedAsync();
		var id = Guid.NewGuid();
		await using (var context = new ExpenseDbContext(new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(ConnectionString).Options))
		{
			new ExpenseItemRepository(context).Add(new ExpenseItem
			{
				Id = id,
				FkExpenseId = ExpenseId,
				FkCategoryId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
				Name = "Synthetic item",
				UnitPrice = price,
				Quantity = 0.735m,
				Discount = discount,
				Unit = "kg",
				IsSystemGenerated = true
			});
			await context.SaveChangesAsync();
		}
		await using var read = new ExpenseDbContext(new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(ConnectionString).Options);
		var item = await read.ExpenseItems.SingleAsync(x => x.Id == id);
		Assert.Equal((decimal)price, item.UnitPrice);
		Assert.Equal((decimal)discount, item.Discount);
		Assert.Equal(0.735m, item.Quantity);
		Assert.Equal("kg", item.Unit);
		Assert.True(item.IsSystemGenerated);
	}

	[PostgresFact]
	public async Task DatabaseChecksRejectPositiveDiscountAndNonpositiveQuantity()
	{
		await SeedAsync();
		await AssertConstraintAsync("UPDATE expense.expense_item SET discount = 1", PostgresErrorCodes.CheckViolation, "c_expense_item_discount_nonpositive");
		await AssertConstraintAsync("UPDATE expense.expense_item SET quantity = 0", PostgresErrorCodes.CheckViolation, "c_expense_item_quantity_above_zero");
		await AssertConstraintAsync("UPDATE expense.expense_item SET quantity = -1", PostgresErrorCodes.CheckViolation, "c_expense_item_quantity_above_zero");
	}

	[PostgresFact]
	public async Task LegacyAssignmentIsRemovedAndSourceUniquenessSurvives()
	{
		await SeedAsync();
		Assert.Equal(0L, await ScalarAsync<long>("""
			SELECT count(*) FROM information_schema.columns
			WHERE table_schema = 'expense' AND table_name = 'expense_item' AND column_name = 'fk_user_id'
			"""));
		Assert.Equal(0L, await ScalarAsync<long>("""
			SELECT count(*) FROM pg_constraint WHERE conrelid = 'expense.expense_item'::regclass
			AND conname IN ('c_expense_item_fk_user_id', 'c_expense_item_unit_price_above_zero')
			"""));
		await AssertConstraintAsync("""
			UPDATE expense.expense SET fk_receipt_id = '60000000-0000-0000-0000-000000000001'
			WHERE _id = '70000000-0000-0000-0000-000000000002'
			""", PostgresErrorCodes.UniqueViolation, "c_expense_unique_fk_receipt_id");
		await AssertConstraintAsync("""
			UPDATE expense.expense SET fk_bank_transaction_id = '50000000-0000-0000-0000-000000000001'
			WHERE _id = '70000000-0000-0000-0000-000000000002'
			""", PostgresErrorCodes.UniqueViolation, "c_expense_unique_fk_bank_transaction_id");
	}

	[PostgresFact]
	public async Task SquashedDowngradeRefusesExistingExpensesWithoutLosingData()
	{
		await SeedAsync();
		var error = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(PreviousMigration));
		Assert.Contains("requires empty expenses", error.MessageText);
		Assert.Equal(7L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense_item WHERE discount = 0"));
		await using var context = OpenDatabase();
		Assert.Contains(await context.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_CompleteExpenseModel", StringComparison.Ordinal));
	}

	[PostgresFact]
	public async Task EmptySchemaCanDowngradeAndUpgradeAgain()
	{
		await MigrateAsync();
		await MigrateAsync(PreviousMigration);
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM pg_constraint WHERE conname = 'c_expense_item_unit_price_above_zero'"));
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM pg_constraint WHERE conname = 'c_expense_item_fk_user_id'"));
		Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'expense' AND table_name = 'expense' AND column_name = 'fk_payer_id'"));
		await MigrateAsync();
		await SeedAsync();
		Assert.Equal(7L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense_item WHERE discount = 0"));
	}
}
