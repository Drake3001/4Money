using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.User.Database;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class SettlementNotificationTests : PostgresFixture
{
	private static readonly Guid Anna = Guid.Parse("10000000-0000-0000-0000-000000000001");
	private static readonly Guid Bartek = Guid.Parse("10000000-0000-0000-0000-000000000002");
	private static readonly Guid Celina = Guid.Parse("10000000-0000-0000-0000-000000000003");
	private static readonly Guid Group = Guid.Parse("20000000-0000-0000-0000-000000000001");

	private AppDbContext OpenModel() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

	private async Task Run(Func<GroupingDbContext, NotificationsDbContext, GroupingTransactionCoordinator, GroupSettlementService, Task> test)
	{
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var connection = new NpgsqlConnection(ConnectionString);
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection).Options);
		await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection).Options);
		await using var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		var tx = new GroupingTransactionCoordinator(db, inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
		var groups = new GroupRepository(db);
		var members = new GroupMemberRepository(db);
		var notify = new NotificationService(new NotificationRepository(inbox), new NotificationRecipientRepository(inbox), inbox, new UserLookupService(users));
		var service = new GroupSettlementService(db, groups, members, new GroupSettlementRepository(db), new GroupMemberAggregateService(groups, members, tx), tx, TimeProvider.System, null!, notify, new UserDisplayNameLookup(users));
		await test(db, inbox, tx, service);
	}

	private static CreateGroupSettlementRequest Request(Guid? from = null, Guid? to = null) => new()
	{
		FromUserId = from ?? Anna,
		ToUserId = to ?? Bartek,
		Amount = 5,
		Method = SettlementMethod.Cash,
		SettledAt = DateTimeOffset.UtcNow.AddMinutes(-1)
	};

	[PostgresTheory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	public Task Create_notifies_only_recipient_including_former_member(bool administrator, bool former) => Run(async (db, inbox, tx, service) =>
	{
		if (administrator)
			db.GroupMembers.Add(new GroupMember { FkUserId = Celina, FkGroupId = Group, JoinedAt = DateTime.UtcNow });
		if (former)
			(await db.GroupMembers.SingleAsync(m => m.FkUserId == Bartek && m.FkGroupId == Group)).IsMember = false;
		await db.SaveChangesAsync();
		Assert.Equal(201, (await service.CreateAsync(Anna, Group, Request(administrator ? Celina : Anna))).Status);
		var notification = await inbox.Notifications.Include(n => n.NotificationRecipients).SingleAsync(n => n.Type == NotificationType.SettlementReceived && n.NotificationRecipients.Any(r => r.FkUserId == Bartek));
		Assert.Equal(Group, notification.FkGroupId);
		Assert.Null(notification.FkExpenseId);
		Assert.Equal("Zapisano rozliczenie", notification.Title);
		Assert.Equal($"Zapisano rozliczenie od {(administrator ? "Celina" : "Anna")} Testowa dla Ciebie na kwotę 5,00 zł w grupie „Dom”.", notification.Text);
		var recipient = Assert.Single(notification.NotificationRecipients);
		Assert.Equal(Bartek, recipient.FkUserId);
		Assert.Null(recipient.ReadAt);
		var page = await new NotificationService(new NotificationRepository(inbox), new NotificationRecipientRepository(inbox), inbox, null!).GetByUserAsync(Bartek);
		Assert.Contains(page.Items, n => n.Id == notification.Id && n.Type == NotificationType.SettlementReceived && !n.IsRead);
	});

	[PostgresTheory]
	[InlineData(true)]
	[InlineData(false)]
	public Task Author_recipient_and_inactive_account_do_not_get_notifications(bool authorRecipient) => Run(async (db, inbox, tx, service) =>
	{
		if (!authorRecipient)
		{
			await using var model = OpenModel();
			await model.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"user\".\"user\" SET is_active = false WHERE _id = {Bartek}");
		}
		var before = await inbox.Notifications.CountAsync();
		Assert.Equal(201, (await service.CreateAsync(Anna, Group, authorRecipient ? Request(Bartek, Anna) : Request())).Status);
		Assert.Equal(before, await inbox.Notifications.CountAsync());
	});

	[PostgresFact]
	public Task Update_delete_and_invalid_create_do_not_notify() => Run(async (db, inbox, tx, service) =>
	{
		var created = await service.CreateAsync(Anna, Group, Request());
		var before = await inbox.Notifications.CountAsync();
		Assert.Equal(200, (await service.UpdateAsync(Anna, Group, created.Value!.Id, new()
		{
			FromUserId = Bartek,
			ToUserId = Anna,
			Amount = 7,
			Method = SettlementMethod.Other,
			SettledAt = DateTimeOffset.UtcNow.AddMinutes(-1)
		})).Status);
		Assert.Equal(204, (await service.DeleteAsync(Anna, Group, created.Value.Id)).Status);
		Assert.Equal(403, (await service.CreateAsync(Bartek, Group, Request())).Status);
		Assert.Equal(before, await inbox.Notifications.CountAsync());
	});

	[PostgresTheory]
	[InlineData("notification")]
	[InlineData("notification_recipient")]
	[InlineData("group_settlements")]
	[InlineData("outer_rejection")]
	public Task Failure_rolls_back_settlement_aggregates_and_entire_notification(string failure) => Run(async (db, inbox, tx, service) =>
	{
		await using var model = OpenModel();
		var settlements = await model.GroupSettlements.CountAsync();
		var notifications = await model.Set<Notification>().CountAsync();
		var recipients = await model.Set<NotificationRecipient>().CountAsync();
		var before = await model.Set<GroupMember>().AsNoTracking().Where(m => m.FkGroupId == Group).OrderBy(m => m.FkUserId)
			.Select(m => new { m.FkUserId, m.TotalSettlementsSent, m.TotalSettlementsReceived }).ToArrayAsync();
		if (failure == "outer_rejection")
		{
			Assert.False(await tx.ExecuteAsync(async ct =>
			{
				Assert.Equal(201, (await service.CreateAsync(Anna, Group, Request(), ct)).Status);
				Assert.Equal(notifications + 1, await inbox.Notifications.CountAsync(ct));
				return false;
			}, success => success));
		}
		else
		{
			var schema = failure == "group_settlements" ? "grouping" : "notifications";
			var triggerSql = $"""
CREATE FUNCTION public.reject_settlement_test() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'synthetic write failure'; END; $$;
CREATE TRIGGER reject_settlement_test BEFORE INSERT ON {schema}.{failure}
FOR EACH ROW EXECUTE FUNCTION public.reject_settlement_test();
""";
			await model.Database.ExecuteSqlRawAsync(triggerSql);
			await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(Anna, Group, Request()));
		}
		Assert.Equal(settlements, await model.GroupSettlements.CountAsync());
		Assert.Equal(notifications, await model.Set<Notification>().CountAsync());
		Assert.Equal(recipients, await model.Set<NotificationRecipient>().CountAsync());
		Assert.Equal(before, await model.Set<GroupMember>().AsNoTracking().Where(m => m.FkGroupId == Group).OrderBy(m => m.FkUserId)
			.Select(m => new { m.FkUserId, m.TotalSettlementsSent, m.TotalSettlementsReceived }).ToArrayAsync());
	});

	[PostgresFact]
	public async Task Grouping_migration_owns_type_constraint_and_downgrade_preserves_history()
	{
		await using var model = OpenModel();
		var migrator = model.GetService<IMigrator>();
		var migrations = model.Database.GetMigrations().ToArray();
		var index = Array.FindIndex(migrations, m => m.EndsWith("_AddGroupingModel", StringComparison.Ordinal));
		await migrator.MigrateAsync(migrations[index]); // Roll back only temporary Expense.
		var id = Guid.NewGuid();
		await model.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO notifications.notification (_id, title, text, type) VALUES ({id}, 'Settlement', 'Synthetic', 5)");
		await migrator.MigrateAsync(migrations[index - 1]);
		Assert.Equal(NotificationType.General, (await model.Set<Notification>().AsNoTracking().SingleAsync(n => n.Id == id)).Type);
		await Assert.ThrowsAsync<PostgresException>(() => model.Database.ExecuteSqlInterpolatedAsync($"UPDATE notifications.notification SET type = 5 WHERE _id = {id}"));
		await migrator.MigrateAsync(migrations[index]);
		await model.Database.ExecuteSqlInterpolatedAsync($"UPDATE notifications.notification SET type = 5 WHERE _id = {id}");
		await migrator.MigrateAsync();
		Assert.Equal(NotificationType.SettlementReceived, (await model.Set<Notification>().AsNoTracking().SingleAsync(n => n.Id == id)).Type);
		await Assert.ThrowsAsync<PostgresException>(() => model.Database.ExecuteSqlInterpolatedAsync($"UPDATE notifications.notification SET type = 6 WHERE _id = {id}"));
		Assert.False(model.Database.HasPendingModelChanges());
	}
}
