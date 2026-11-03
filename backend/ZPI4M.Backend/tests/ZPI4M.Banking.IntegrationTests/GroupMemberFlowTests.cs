using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.DTOs.Members;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.Database;
namespace ZPI4M.Banking.IntegrationTests;

public sealed class GroupMemberFlowTests : PostgresFixture
{
	private static readonly Guid Admin = Guid.Parse("10000000-0000-0000-0000-000000000001");
	private static readonly Guid Member = Guid.Parse("10000000-0000-0000-0000-000000000002");
	private static readonly Guid Group = Guid.Parse("20000000-0000-0000-0000-000000000001");
	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Ending_membership_preserves_history_and_can_be_repeated(bool removal)
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var connection = new NpgsqlConnection(ConnectionString);
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection).Options);
		await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection).Options);
		var coordinator = new GroupingTransactionCoordinator(db, inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
		var service = new GroupMemberService(new GroupRepository(db), new GroupMemberRepository(db), db, coordinator);
		var before = (await service.GetAsync(Admin, Group, Member)).Value!;
		Assert.Equal(204, (removal ? await service.RemoveAsync(Admin, Group, Member) : await service.LeaveAsync(Member, Group)).Status);
		Assert.Equal(204, (removal ? await service.RemoveAsync(Admin, Group, Member) : await service.LeaveAsync(Member, Group)).Status);
		var after = (await service.GetAsync(Admin, Group, Member)).Value!;
		Assert.Equal(before with { IsMember = false }, after);
		Assert.Single((await service.ListAsync(Admin, Group, new GroupMemberListRequest { Membership = "former" })).Value!.Items);
		Assert.Single((await service.ListAsync(Admin, Group, new GroupMemberListRequest())).Value!.Items);
		Assert.Equal(2, (await service.ListAsync(Admin, Group, new GroupMemberListRequest { Membership = "all" })).Value!.TotalCount);
		Assert.Equal(404, (await service.GetAsync(Member, Group, Admin)).Status);
	}
	[PostgresFact]
	public async Task Administrator_cannot_leave_and_refusal_rolls_back_outer_write()
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var connection = new NpgsqlConnection(ConnectionString);
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection).Options);
		await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection).Options);
		var coordinator = new GroupingTransactionCoordinator(db, inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
		var service = new GroupMemberService(new GroupRepository(db), new GroupMemberRepository(db), db, coordinator);
		await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAsync(async ct =>
		{
			var group = await db.Groups.SingleAsync(g => g.Id == Group, ct);
			group.Description = "should rollback";
			await db.SaveChangesAsync(ct);
			Assert.Equal(409, (await service.LeaveAsync(Admin, Group, ct)).Status);
			return true;
		}, r => r));
		await using var verify = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		Assert.Null((await verify.Groups.FindAsync(Group))!.Description);
		Assert.True((await verify.GroupMembers.SingleAsync(m => m.FkGroupId == Group && m.FkUserId == Admin)).IsMember);
	}

	[PostgresTheory]
	[InlineData(0, 403)]
	[InlineData(1, 404)]
	[InlineData(2, 409)]
	[InlineData(3, 404)]
	[InlineData(4, 404)]
	public async Task Mutation_permissions_are_enforced(int scenario, int expected)
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var connection = new NpgsqlConnection(ConnectionString);
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection).Options);
		await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection).Options);
		var coordinator = new GroupingTransactionCoordinator(db, inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
		var service = new GroupMemberService(new GroupRepository(db), new GroupMemberRepository(db), db, coordinator);

		var result = scenario switch
		{
			0 => await service.RemoveAsync(Member, Group, Admin),
			1 => await service.RemoveAsync(Guid.NewGuid(), Group, Member),
			2 => await service.RemoveAsync(Admin, Group, Admin),
			3 => await service.LeaveAsync(Guid.NewGuid(), Group),
			_ => await service.RemoveAsync(Admin, Group, Guid.NewGuid())
		};
		Assert.Equal(expected, result.Status);
	}

	[PostgresFact]
	public async Task Concurrent_leave_and_transfer_keep_administrator_an_active_member()
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		async Task<int> Change(bool leave)
		{
			await using var connection = new NpgsqlConnection(ConnectionString);
			await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection).Options);
			await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection).Options);
			var tx = new GroupingTransactionCoordinator(db, inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
			if (leave) return (await new GroupMemberService(new GroupRepository(db), new GroupMemberRepository(db), db, tx).LeaveAsync(Member, Group)).Status;
			return (await new ZPI4M.Grouping.Services.Groups.GroupService(db, new Users(), null!, tx, new ZPI4M.Grouping.Repositories.GroupRepository.GroupRepository(db), new ZPI4M.Grouping.Repositories.GroupMemberRepository.GroupMemberRepository(db)).TransferAdministratorAsync(Admin, Group, Member)).Status;
		}
		var results = await Task.WhenAll(Change(true), Change(false));
		Assert.True(results.SequenceEqual(new[] { 204, 409 }) || results.SequenceEqual(new[] { 409, 200 }));
		await using var verify = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		var group = await verify.Groups.SingleAsync(g => g.Id == Group);
		Assert.True(await verify.GroupMembers.AnyAsync(m => m.FkGroupId == Group && m.FkUserId == group.AdminUserId && m.IsMember));
	}
	private sealed class Users : ZPI4M.User.Services.Lookup.IUserLookupService
	{
		public Task<IReadOnlyCollection<Guid>> GetActiveUserIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => Task.FromResult(ids);
	}
}
