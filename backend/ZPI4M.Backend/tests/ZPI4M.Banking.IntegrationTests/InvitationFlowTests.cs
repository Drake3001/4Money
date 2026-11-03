using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.DTOs.Invitations;
using ZPI4M.Grouping.DTOs.Members;
using ZPI4M.Grouping.Repositories.GroupInvitationRepository;
using ZPI4M.Grouping.Services.Groups;
using ZPI4M.Grouping.Services.Invitations;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.User.Services.Lookup;
namespace ZPI4M.Banking.IntegrationTests;

public sealed class InvitationFlowTests : PostgresFixture
{
	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Accept_consumes_once_notifies_and_reactivates_without_resetting_history(bool removed)
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(model.Database.GetConnectionString()!, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var connection = new NpgsqlConnection(ConnectionString);
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection).Options);
		await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection).Options);
		var users = new Users();
		var notify = new NotificationService(new NotificationRepository(inbox), new NotificationRecipientRepository(inbox), inbox, users);
		var coordinator = new ZPI4M.Grouping.Services.Transactions.GroupingTransactionCoordinator(db, inbox, NullLogger<ZPI4M.Grouping.Services.Transactions.GroupingTransactionCoordinator>.Instance);
		var service = new GroupInvitationService(db, new GroupInvitationRepository(db), notify, users, null!, TimeProvider.System, coordinator, new ZPI4M.Grouping.Repositories.GroupRepository.GroupRepository(db), new ZPI4M.Grouping.Repositories.GroupMemberRepository.GroupMemberRepository(db));
		var admin = Guid.Parse("10000000-0000-0000-0000-000000000001");
		var member = Guid.Parse("10000000-0000-0000-0000-000000000002");
		var group = Guid.Parse("20000000-0000-0000-0000-000000000001");
		var previous = await db.GroupMembers.SingleAsync(m => m.FkUserId == member && m.FkGroupId == group);
		var members = new ZPI4M.Grouping.Services.Members.GroupMemberService(new ZPI4M.Grouping.Repositories.GroupRepository.GroupRepository(db), new ZPI4M.Grouping.Repositories.GroupMemberRepository.GroupMemberRepository(db), db, coordinator);
		Assert.Equal(204, (removed ? await members.RemoveAsync(admin, group, member) : await members.LeaveAsync(member, group)).Status);
		var joined = previous.JoinedAt; var paid = previous.TotalPaid;
		var response = await service.CreateAsync(admin, group, new CreateGroupInvitationRequest { MaxUses = 1, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
		var created = Assert.IsType<CreateGroupInvitationResponse>(response.Value);
		var count = await inbox.Notifications.CountAsync();
		await model.Database.ExecuteSqlRawAsync("""
   CREATE FUNCTION notifications.reject_test_notification() RETURNS trigger LANGUAGE plpgsql AS $$
   BEGIN RAISE EXCEPTION 'synthetic notification failure'; END; $$;
   CREATE TRIGGER reject_test_notification BEFORE INSERT ON notifications.notification
   FOR EACH ROW EXECUTE FUNCTION notifications.reject_test_notification();
   """);
		var failure = await Assert.ThrowsAsync<DbUpdateException>(() => service.AcceptAsync(member, created.Token));
		Assert.Equal("P0001", Assert.IsType<PostgresException>(failure.InnerException).SqlState);
		Assert.False(await db.GroupMembers.AsNoTracking().Where(m => m.FkUserId == member && m.FkGroupId == group).Select(m => m.IsMember).SingleAsync());
		Assert.Equal(0, await db.GroupInvitations.AsNoTracking().Where(i => i.Id == created.Invitation.Id).Select(i => i.UseCount).SingleAsync());
		Assert.Equal(count, await inbox.Notifications.CountAsync());
		await model.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_test_notification ON notifications.notification; DROP FUNCTION notifications.reject_test_notification();");
		await using var retryConnection = new NpgsqlConnection(ConnectionString);
		await using var retryDb = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(retryConnection).Options);
		await using var retryInbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(retryConnection).Options);
		var retryCoordinator = new ZPI4M.Grouping.Services.Transactions.GroupingTransactionCoordinator(retryDb, retryInbox, NullLogger<ZPI4M.Grouping.Services.Transactions.GroupingTransactionCoordinator>.Instance);
		var retryNotify = new NotificationService(new NotificationRepository(retryInbox), new NotificationRecipientRepository(retryInbox), retryInbox, users);
		service = new GroupInvitationService(retryDb, new GroupInvitationRepository(retryDb), retryNotify, users, null!, TimeProvider.System, retryCoordinator, new ZPI4M.Grouping.Repositories.GroupRepository.GroupRepository(retryDb), new ZPI4M.Grouping.Repositories.GroupMemberRepository.GroupMemberRepository(retryDb));
		var accepted = Assert.IsType<GroupMemberResponse>((await service.AcceptAsync(member, created.Token)).Value);
		Assert.True(accepted.IsMember);
		Assert.Equal(joined, accepted.JoinedAt); Assert.Equal(paid, accepted.TotalPaid);
		Assert.Equal(1, (await retryDb.GroupInvitations.SingleAsync(i => i.Id == created.Invitation.Id)).UseCount);
		Assert.Equal(count + 1, await inbox.Notifications.CountAsync());
		Assert.Equal(200, (await service.AcceptAsync(member, created.Token)).Status);
		Assert.Equal(count + 1, await inbox.Notifications.CountAsync());
		Assert.Equal(204, (await service.RevokeAsync(admin, group, created.Invitation.Id)).Status);
		Assert.Equal(404, (await service.AcceptAsync(member, created.Token)).Status);
	}
	private sealed class Users : IUserLookupService
	{
		public Task<IReadOnlyCollection<Guid>> GetActiveUserIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => Task.FromResult(ids);
	}
}
