using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Expense.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Models;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Models;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class GroupingModelContractTests
{
	private const string Connection = "Host=127.0.0.1;Database=unused;Username=unused";

	[Fact]
	public void SnapshotMatchesRuntimeAndMigrationGuardsPrecedeDdl()
	{
		using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options);
		Assert.False(db.Database.HasPendingModelChanges());
		var assembly = db.GetService<IMigrationsAssembly>();
		var pair = assembly.Migrations.Single(m => m.Key.EndsWith("_AddGroupingModel", StringComparison.Ordinal));
		Assert.EndsWith("_AddGroupingModel", pair.Key);
		var migration = assembly.CreateMigration(pair.Value, db.Database.ProviderName!);
		var latest = assembly.Migrations.Last();
		var target = db.GetService<IModelRuntimeInitializer>().Initialize(assembly.CreateMigration(latest.Value, db.Database.ProviderName!).TargetModel, designTime: true);
		Assert.False(db.GetService<IMigrationsModelDiffer>().HasDifferences(target.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
		foreach (var operations in new[] { migration.UpOperations, migration.DownOperations })
		{
			var guard = Assert.IsType<SqlOperation>(operations[0]);
			Assert.False(guard.SuppressTransaction);
			Assert.Contains("IN ACCESS EXCLUSIVE MODE", guard.Sql);
			Assert.Contains("RAISE EXCEPTION", guard.Sql);
			Assert.Contains("grouping.", guard.Sql);
		}
		Assert.DoesNotContain(migration.UpOperations.OfType<AddForeignKeyOperation>(), op => op.Name.StartsWith("c_expense_", StringComparison.Ordinal));
		Assert.Equal(3, migration.UpOperations.OfType<CreateTableOperation>().Count());
		Assert.Equal(3, migration.DownOperations.OfType<DropTableOperation>().Count());
		var sql = db.GetService<IMigrator>().GenerateScript(db.Database.GetMigrations().TakeWhile(m => m != pair.Key).Last());
		Assert.True(sql.IndexOf("RAISE EXCEPTION", StringComparison.Ordinal) < sql.IndexOf("ALTER TABLE", StringComparison.Ordinal));
	}

	[Fact]
	public void ModuleModelsStayIsolatedAndNewForeignKeysUseNoAction()
	{
		using var grouping = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(Connection).Options);
		using var expense = new ExpenseDbContext(new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(Connection).Options);
		using var limits = new LimitsDbContext(new DbContextOptionsBuilder<LimitsDbContext>().UseNpgsql(Connection).Options);
		foreach (var db in new DbContext[] { grouping, expense, limits })
			Assert.All(db.Model.GetEntityTypes(), e => Assert.StartsWith(db.GetType().Namespace!.Replace(".Database", ".Models", StringComparison.Ordinal), e.ClrType.Namespace));
		using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options);
		foreach (var type in new[] { typeof(GroupSettlement), typeof(GroupInvitation), typeof(GroupLimit), typeof(Group), typeof(GroupMember), typeof(ZPI4M.Expense.Models.ExpenseShare), typeof(ZPI4M.Expense.Models.Expense) })
			Assert.All(model.Model.FindEntityType(type)!.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
		var member = model.Model.FindEntityType(typeof(GroupMember))!;
		Assert.Equal("group_user", member.GetTableName());
		Assert.Null(member.FindProperty("IsAdmin"));
		var isMember = member.FindProperty(nameof(GroupMember.IsMember))!;
		Assert.Equal("is_member", isMember.GetColumnName());
		Assert.False(isMember.IsNullable);
		Assert.Equal(true, isMember.GetDefaultValue());
		Assert.True(new GroupMember().IsMember);
		foreach (var property in new[] { "TotalPaid", "TotalShare", "TotalSettlementsSent", "TotalSettlementsReceived" })
		{
			var mapped = member.FindProperty(property)!;
			Assert.Equal(18, mapped.GetPrecision());
			Assert.Equal(2, mapped.GetScale());
			Assert.Equal(0m, mapped.GetDefaultValue());
		}
	}
}
