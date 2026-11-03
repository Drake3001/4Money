using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using ZPI4M.Database;
namespace ZPI4M.Banking.IntegrationTests;

public sealed class MigrationSplitContractTests
{
	[Fact]
	public void Grouping_target_keeps_baseline_expense_and_temporary_migration_is_last()
	{
		using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
		var migrations = db.GetService<IMigrationsAssembly>();
		var group = migrations.Migrations.Single(m => m.Key.EndsWith("_AddGroupingModel", StringComparison.Ordinal));
		var temporary = migrations.Migrations.Last();
		Assert.EndsWith("_AddTemporaryExpenseAggregationSupport", temporary.Key);
		var first = migrations.CreateMigration(group.Value, db.Database.ProviderName!);
		var expense = first.TargetModel.FindEntityType("ZPI4M.Expense.Models.Expense")!;
		Assert.Null(expense.FindProperty("FkPayerId"));
		Assert.NotNull(expense.FindProperty("Name"));
		Assert.NotNull(first.TargetModel.FindEntityType("ZPI4M.Expense.Models.ExpenseShare")!.FindProperty("Percentage"));
		foreach (var op in first.UpOperations)
		{
			Assert.NotEqual("expense", op.GetType().GetProperty("Schema")?.GetValue(op));
		}
		var second = migrations.CreateMigration(temporary.Value, db.Database.ProviderName!);
		Assert.NotNull(second.TargetModel.FindEntityType("ZPI4M.Expense.Models.Expense")!.FindProperty("FkPayerId"));
		Assert.NotNull(second.TargetModel.FindEntityType("ZPI4M.Expense.Models.ExpenseShare")!.FindProperty("Amount"));
		foreach (var op in second.UpOperations)
		{
			var schema = op.GetType().GetProperty("Schema")?.GetValue(op);
			if (schema is not null) Assert.Equal("expense", schema);
		}
		Assert.False(db.Database.HasPendingModelChanges());
	}
}
