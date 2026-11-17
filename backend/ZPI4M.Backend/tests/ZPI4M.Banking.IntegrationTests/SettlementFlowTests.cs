using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.DTOs.Settlements;
using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Repositories.GroupSettlementRepository;
using ZPI4M.Grouping.Services.Aggregates;
using ZPI4M.Grouping.Services.Settlements;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.User.Database;
using ZPI4M.User.Services.Lookup;
namespace ZPI4M.Banking.IntegrationTests;

public sealed class SettlementFlowTests : PostgresFixture
{
	private static readonly Guid Anna = Guid.Parse("10000000-0000-0000-0000-000000000001"), Bartek = Guid.Parse("10000000-0000-0000-0000-000000000002"), Group = Guid.Parse("20000000-0000-0000-0000-000000000001");
	private async Task Run(Func<GroupingDbContext, GroupingTransactionCoordinator, GroupMemberAggregateService, GroupSettlementService, Task> test)
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var connection = new NpgsqlConnection(ConnectionString);
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection).Options);
		await using var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection).Options);
		var tx = new GroupingTransactionCoordinator(db, inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
		var groups = new GroupRepository(db); var members = new GroupMemberRepository(db);
		var aggregates = new GroupMemberAggregateService(groups, members, tx);
		await test(db, tx, aggregates, new GroupSettlementService(db, groups, members, new GroupSettlementRepository(db), aggregates, tx, TimeProvider.System, null!, new NotificationService(new NotificationRepository(inbox), new NotificationRecipientRepository(inbox), inbox, new UserLookupService(users)), new UserDisplayNameLookup(users)));
	}
	private static CreateGroupSettlementRequest Create() => new() { FromUserId = Anna, ToUserId = Bartek, Amount = 5, Method = SettlementMethod.Cash, SettledAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
	[PostgresFact]
	public Task Crud_reverses_old_contribution_and_accepts_former_participant() => Run(async (db, tx, aggregates, service) =>
	{
		var anna = await db.GroupMembers.SingleAsync(m => m.FkGroupId == Group && m.FkUserId == Anna);
		var bartek = await db.GroupMembers.SingleAsync(m => m.FkGroupId == Group && m.FkUserId == Bartek);
		var sent = anna.TotalSettlementsSent; var received = bartek.TotalSettlementsReceived;
		var oldSent = bartek.TotalSettlementsSent; var oldReceived = anna.TotalSettlementsReceived;
		bartek.IsMember = false; await db.SaveChangesAsync();
		var created = await service.CreateAsync(Anna, Group, Create()); Assert.Equal(201, created.Status);
		Assert.Equal(sent + 5, anna.TotalSettlementsSent); Assert.Equal(received + 5, bartek.TotalSettlementsReceived);
		var updated = await service.UpdateAsync(Anna, Group, created.Value!.Id, new() { FromUserId = Bartek, ToUserId = Anna, Amount = 7, Method = SettlementMethod.BankTransfer, SettledAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
		Assert.Equal(200, updated.Status); Assert.Equal(sent, anna.TotalSettlementsSent); Assert.Equal(received, bartek.TotalSettlementsReceived);
		Assert.Equal(oldSent + 7, bartek.TotalSettlementsSent); Assert.Equal(oldReceived + 7, anna.TotalSettlementsReceived);
		Assert.Equal(204, (await service.DeleteAsync(Anna, Group, created.Value.Id)).Status);
		Assert.Equal(oldSent, bartek.TotalSettlementsSent); Assert.Equal(oldReceived, anna.TotalSettlementsReceived);
	});
	[PostgresFact]
	public Task Expense_contribution_updates_all_roles_without_expense_context() => Run(async (db, tx, aggregates, service) =>
	{
		var rows = await db.GroupMembers.Where(m => m.FkGroupId == Group).ToDictionaryAsync(m => m.FkUserId);
		var paid = rows.ToDictionary(p => p.Key, p => p.Value.TotalPaid); var share = rows.ToDictionary(p => p.Key, p => p.Value.TotalShare);
		var first = new ExpenseContribution(Group, Anna, 100, [new(Anna, 60), new(Bartek, 40)]);
		var next = new ExpenseContribution(Group, Bartek, 80, [new(Bartek, 80)]);
		await tx.ExecuteAsync(async ct => { await aggregates.ApplyExpenseChangeAsync(null, first, ct); await db.SaveChangesAsync(ct); return true; }, v => v);
		Assert.Equal(paid[Anna] + 100, rows[Anna].TotalPaid); Assert.Equal(share[Bartek] + 40, rows[Bartek].TotalShare);
		await tx.ExecuteAsync(async ct => { await aggregates.ApplyExpenseChangeAsync(first, next, ct); await db.SaveChangesAsync(ct); return true; }, v => v);
		Assert.Equal(paid[Anna], rows[Anna].TotalPaid); Assert.Equal(paid[Bartek] + 80, rows[Bartek].TotalPaid);
		await tx.ExecuteAsync(async ct => { await aggregates.ApplyExpenseChangeAsync(next, null, ct); await db.SaveChangesAsync(ct); return true; }, v => v);
		foreach (var row in rows) { Assert.Equal(paid[row.Key], row.Value.TotalPaid); Assert.Equal(share[row.Key], row.Value.TotalShare); }
	});
	[PostgresFact]
	public Task Source_failure_rolls_back_aggregate_changes() => Run(async (db, tx, aggregates, service) =>
	{
		var before = await db.GroupMembers.AsNoTracking().Where(m => m.FkGroupId == Group && m.FkUserId == Anna).Select(m => m.TotalSettlementsSent).SingleAsync();
		await SeedScript.ExecuteAsync(ConnectionString, "CREATE FUNCTION grouping.fail_settlement() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic settlement failure'; END; $$; CREATE TRIGGER fail_settlement BEFORE INSERT ON grouping.group_settlements FOR EACH ROW EXECUTE FUNCTION grouping.fail_settlement();");
		await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(Anna, Group, Create()));
		await using var verify = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		Assert.Equal(before, await verify.GroupMembers.Where(m => m.FkGroupId == Group && m.FkUserId == Anna).Select(m => m.TotalSettlementsSent).SingleAsync());
		Assert.Equal(3, await verify.GroupSettlements.CountAsync());
	});
	[PostgresFact]
	public Task Aggregate_requires_coordinator_and_rejects_inconsistent_shares() => Run(async (db, tx, aggregates, service) =>
	{
		await Assert.ThrowsAsync<InvalidOperationException>(() => aggregates.ApplySettlementChangeAsync(null, new(Group, Anna, Bartek, 1)));
		await Assert.ThrowsAsync<AggregateInvariantException>(() => tx.ExecuteAsync(async ct => { await aggregates.ApplyExpenseChangeAsync(null, new(Group, Anna, 5, [new(Anna, 4)]), ct); return true; }, v => v));
	});

	[PostgresTheory]
	[InlineData(0, 403)]
	[InlineData(1, 400)]
	[InlineData(2, 400)]
	[InlineData(3, 400)]
	[InlineData(4, 409)]
	[InlineData(5, 409)]
	public Task Invalid_writes_are_rejected(int scenario, int expected) => Run(async (db, tx, aggregates, service) =>
	{
		var request = new CreateGroupSettlementRequest
		{
			FromUserId = scenario == 5 ? Bartek : Anna,
			ToUserId = scenario == 3 ? Anna : scenario == 4 ? Guid.NewGuid() : scenario == 5 ? Anna : Bartek,
			Amount = scenario == 5 ? 9999999999999999.99m : 5,
			Method = SettlementMethod.Cash,
			SettledAt = scenario == 1 ? DateTimeOffset.UtcNow.AddDays(1) : DateTimeOffset.UtcNow.AddMinutes(-1),
			BankTransactionId = scenario == 2 ? Guid.NewGuid() : null
		};
		Assert.Equal(expected, (await service.CreateAsync(scenario == 0 ? Bartek : Anna, Group, request)).Status);
	});
	[PostgresFact]
	public Task Reads_are_filtered_and_untracked() => Run(async (db, tx, aggregates, service) =>
	{
		var created = await service.CreateAsync(Anna, Group, Create());
		db.ChangeTracker.Clear();
		var page = await service.ListAsync(Anna, Group, new() { FromUserId = Anna, PageSize = 1 });
		Assert.Equal(200, page.Status); Assert.Equal(1, page.Value!.TotalCount);
		Assert.Equal(created.Value!.Id, Assert.Single(page.Value.Items).Id);
		Assert.Equal(200, (await service.GetAsync(Anna, Group, created.Value.Id)).Status);
		Assert.Empty(db.ChangeTracker.Entries());
	});
	[PostgresFact]
	public Task Concurrent_creates_do_not_lose_aggregate_updates() => Run(async (db, tx, aggregates, service) =>
	{
		var before = await db.GroupMembers.AsNoTracking().Where(m => m.FkGroupId == Group && m.FkUserId == Anna).Select(m => m.TotalSettlementsSent).SingleAsync();
		async Task<int> Add()
		{
			await using var connection = new NpgsqlConnection(ConnectionString);
			await using var scope = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection).Options);
			await using var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
			await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection).Options);
			var coordinator = new GroupingTransactionCoordinator(scope, inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
			var groups = new GroupRepository(scope); var members = new GroupMemberRepository(scope);
			var aggregate = new GroupMemberAggregateService(groups, members, coordinator);
			return (await new GroupSettlementService(scope, groups, members, new GroupSettlementRepository(scope), aggregate, coordinator, TimeProvider.System, null!, new NotificationService(new NotificationRepository(inbox), new NotificationRecipientRepository(inbox), inbox, new UserLookupService(users)), new UserDisplayNameLookup(users)).CreateAsync(Anna, Group, Create())).Status;
		}
		var statuses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Add()));
		Assert.All(statuses, status => Assert.Equal(201, status));
		Assert.Equal(before + 25, await db.GroupMembers.AsNoTracking().Where(m => m.FkGroupId == Group && m.FkUserId == Anna).Select(m => m.TotalSettlementsSent).SingleAsync());
	});
}
