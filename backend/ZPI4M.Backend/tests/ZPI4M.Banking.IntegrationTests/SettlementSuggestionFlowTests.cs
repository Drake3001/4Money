using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Repositories.GroupSettlementRepository;
using ZPI4M.Grouping.Services.Aggregates;
using ZPI4M.Grouping.Services.Settlements;
using ZPI4M.Grouping.Services.Suggestions;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.User.Database;
using ZPI4M.User.Services.Lookup;
namespace ZPI4M.Banking.IntegrationTests;

public sealed class SettlementSuggestionFlowTests : PostgresFixture
{
	[PostgresFact]
	public async Task Suggestions_include_former_members_do_not_write_and_recorded_plan_clears_balances()
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var connection = new NpgsqlConnection(ConnectionString);
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection).Options);
		await using var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection).Options);
		var admin = Guid.Parse("10000000-0000-0000-0000-000000000001");
		var former = Guid.Parse("10000000-0000-0000-0000-000000000002");
		var group = Guid.Parse("20000000-0000-0000-0000-000000000001");
		(await db.GroupMembers.SingleAsync(m => m.FkGroupId == group && m.FkUserId == former)).IsMember = false;
		await db.SaveChangesAsync(); db.ChangeTracker.Clear();
		var groups = new GroupRepository(db); var members = new GroupMemberRepository(db);
		var service = new GroupSettlementSuggestionService(groups, new SettlementSuggestionCalculator(), TimeProvider.System);
		var count = await db.GroupSettlements.CountAsync();
		var result = await service.GetAsync(admin, group);
		Assert.Equal(200, result.Status); Assert.Equal(530, result.Value!.TotalAmount);
		Assert.Contains(result.Value.Balances, b => b.UserId == former && !b.IsMember);
		Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(count, await db.GroupSettlements.CountAsync());
		Assert.Equal(404, (await service.GetAsync(former, group)).Status);
		var coordinator = new GroupingTransactionCoordinator(db, inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
		var writes = new GroupSettlementService(db, groups, members, new GroupSettlementRepository(db), new GroupMemberAggregateService(groups, members, coordinator), coordinator, TimeProvider.System, null!, new NotificationService(new NotificationRepository(inbox), new NotificationRecipientRepository(inbox), inbox, new UserLookupService(users)), new UserDisplayNameLookup(users));
		foreach (var suggestion in result.Value.Suggestions)
			Assert.Equal(201, (await writes.CreateAsync(admin, group, new() { FromUserId = suggestion.FromUserId, ToUserId = suggestion.ToUserId, Amount = suggestion.Amount, Method = SettlementMethod.Cash, SettledAt = DateTimeOffset.UtcNow.AddSeconds(-1) })).Status);
		Assert.Empty((await service.GetAsync(admin, group)).Value!.Suggestions);
		var member = await db.GroupMembers.SingleAsync(m => m.FkGroupId == group && m.FkUserId == admin);
		member.TotalPaid += 0.01m; await db.SaveChangesAsync();
		Assert.Equal(409, (await service.GetAsync(admin, group)).Status);
		(await db.Groups.SingleAsync(g => g.Id == group)).IsArchived = true; await db.SaveChangesAsync();
		Assert.Equal(404, (await service.GetAsync(admin, group)).Status);
	}

	[PostgresFact]
	public async Task Read_sees_one_snapshot_while_two_balances_are_updated()
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		var admin = Guid.Parse("10000000-0000-0000-0000-000000000001");
		var group = Guid.Parse("20000000-0000-0000-0000-000000000001");
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		var service = new GroupSettlementSuggestionService(new GroupRepository(db), new SettlementSuggestionCalculator(), TimeProvider.System);
		await using var writer = new NpgsqlConnection(ConnectionString);
		await writer.OpenAsync(); await using var transaction = await writer.BeginTransactionAsync();
		await using (var first = new NpgsqlCommand("UPDATE grouping.group_user SET total_paid=total_paid+1 WHERE group_id='20000000-0000-0000-0000-000000000001' AND user_id='10000000-0000-0000-0000-000000000001'", writer, transaction))
			await first.ExecuteNonQueryAsync();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var before = await service.GetAsync(admin, group, timeout.Token);
		Assert.Equal(200, before.Status); Assert.Equal(530, before.Value!.TotalAmount);
		await using (var second = new NpgsqlCommand("UPDATE grouping.group_user SET total_share=total_share+1 WHERE group_id='20000000-0000-0000-0000-000000000001' AND user_id='10000000-0000-0000-0000-000000000002'", writer, transaction))
			await second.ExecuteNonQueryAsync();
		await transaction.CommitAsync();
		var after = await service.GetAsync(admin, group);
		Assert.Equal(200, after.Status); Assert.Equal(531, after.Value!.TotalAmount);
		Assert.Empty(db.ChangeTracker.Entries());
	}
}
