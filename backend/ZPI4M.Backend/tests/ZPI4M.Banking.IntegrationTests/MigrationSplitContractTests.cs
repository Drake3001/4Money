using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using ZPI4M.Database;
namespace ZPI4M.Banking.IntegrationTests;

public sealed class MigrationSplitContractTests
{
	[Fact]
	public void GroupingTargetKeepsBaselineExpenseAndBankingFollowsCompleteExpense()
	{
		using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
		var migrations = db.GetService<IMigrationsAssembly>();
		var group = migrations.Migrations.Single(m => m.Key.EndsWith("_AddGroupingModel", StringComparison.Ordinal));
		var finalExpense = migrations.Migrations.Single(m => m.Key.EndsWith("_CompleteExpenseModel", StringComparison.Ordinal));
		var banking = migrations.Migrations.Single(m => m.Key.EndsWith("_AddBankTransactionAssignments", StringComparison.Ordinal));
		Assert.True(string.CompareOrdinal(group.Key, finalExpense.Key) < 0);
		Assert.True(string.CompareOrdinal(finalExpense.Key, banking.Key) < 0);
		Assert.DoesNotContain(migrations.Migrations.Keys, key => key.EndsWith("_AddTemporaryExpenseAggregationSupport", StringComparison.Ordinal));
		var bankingTarget = migrations.CreateMigration(banking.Value, db.Database.ProviderName!).TargetModel;
		Assert.NotNull(bankingTarget.FindEntityType("ZPI4M.Expense.Models.ExpenseShare")!.FindProperty("Percentage"));
		Assert.NotNull(bankingTarget.FindEntityType("ZPI4M.Banking.Models.BankTransactionAssignment"));
		var first = migrations.CreateMigration(group.Value, db.Database.ProviderName!);
		var expense = first.TargetModel.FindEntityType("ZPI4M.Expense.Models.Expense")!;
		Assert.Null(expense.FindProperty("FkPayerId"));
		Assert.NotNull(expense.FindProperty("Name"));
		Assert.NotNull(first.TargetModel.FindEntityType("ZPI4M.Expense.Models.ExpenseShare")!.FindProperty("Percentage"));
		foreach (var op in first.UpOperations)
		{
			Assert.NotEqual("expense", op.GetType().GetProperty("Schema")?.GetValue(op));
		}
		var second = migrations.CreateMigration(finalExpense.Value, db.Database.ProviderName!);
		Assert.NotNull(second.TargetModel.FindEntityType("ZPI4M.Expense.Models.Expense")!.FindProperty("FkPayerId"));
		Assert.NotNull(second.TargetModel.FindEntityType("ZPI4M.Expense.Models.ExpenseShare")!.FindProperty("Percentage"));
		foreach (var op in second.UpOperations)
		{
			var schema = op.GetType().GetProperty("Schema")?.GetValue(op);
			if (schema is not null) Assert.Equal("expense", schema);
		}
		Assert.False(db.Database.HasPendingModelChanges());
	}
}
