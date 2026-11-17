using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.Database;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class GroupingTransactionTests : PostgresFixture
{
	private async Task Run(Func<GroupingDbContext, NotificationsDbContext, GroupingTransactionCoordinator, Task> test)
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(model.Database.GetConnectionString()!, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var connection = new NpgsqlConnection(ConnectionString);
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection).Options);
		await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection).Options);
		var coordinator = new GroupingTransactionCoordinator(db, inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
		await test(db, inbox, coordinator);
	}

	[PostgresTheory]
	[InlineData(true)]
	[InlineData(false)]
	public Task Group_and_invitation_share_final_outcome(bool succeed) => Run(async (db, inbox, c) =>
	{
		var groups = new ZPI4M.Grouping.Services.Groups.GroupService(db, null!, null!, c, new ZPI4M.Grouping.Repositories.GroupRepository.GroupRepository(db), new ZPI4M.Grouping.Repositories.GroupMemberRepository.GroupMemberRepository(db));
		var invitations = new ZPI4M.Grouping.Services.Invitations.GroupInvitationService(db, new ZPI4M.Grouping.Repositories.GroupInvitationRepository.GroupInvitationRepository(db), null!, null!, null!, TimeProvider.System, c, new ZPI4M.Grouping.Repositories.GroupRepository.GroupRepository(db), new ZPI4M.Grouping.Repositories.GroupMemberRepository.GroupMemberRepository(db), null!);
		var user = Guid.Parse("10000000-0000-0000-0000-000000000001");
		Guid groupId = default;
		await c.ExecuteAsync(async ct =>
		{
			var group = await groups.CreateAsync(user, new ZPI4M.Grouping.DTOs.Groups.CreateGroupRequest { Name = "Transactional", CurrencyCode = "PLN" }, ct);
			groupId = group.Value!.Id;
			var invitation = await invitations.CreateAsync(user, groupId, new ZPI4M.Grouping.DTOs.Invitations.CreateGroupInvitationRequest { MaxUses = 1, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) }, ct);
			Assert.Equal(201, invitation.Status);
			return succeed;
		}, v => v);
		await using var verify = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		Assert.Equal(succeed, await verify.Groups.AnyAsync(g => g.Id == groupId));
		Assert.Equal(succeed, await verify.GroupMembers.AnyAsync(m => m.FkGroupId == groupId));
		Assert.Equal(succeed, await verify.GroupInvitations.AnyAsync(i => i.GroupId == groupId));
	});
	[PostgresFact]
	public Task Nested_success_commits_once_and_runs_cleanup_only_after_commit() => Run(async (db, inbox, c) =>
	{
		var group = await db.Groups.FirstAsync();
		var cleaned = false;
		await c.ExecuteAsync(async ct =>
		{
			group.Description = "committed";
			await db.SaveChangesAsync(ct);
			var transaction = db.Database.CurrentTransaction;
			await c.ExecuteAsync(async inner =>
			{
				Assert.Same(transaction, db.Database.CurrentTransaction);
				await c.EnlistNotificationsAsync(inner);
				c.AfterCommit(() => { cleaned = true; return Task.CompletedTask; });
				return true;
			}, v => v, ct);
			Assert.False(cleaned);
			return true;
		}, v => v);
		Assert.True(cleaned);
		Assert.Null(inbox.Database.CurrentTransaction);
		await using var verify = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		Assert.Equal("committed", (await verify.Groups.FindAsync(group.Id))!.Description);
	});
	[PostgresFact]
	public Task Ignored_nested_refusal_rolls_back_without_clearing_caller_entities() => Run(async (db, inbox, c) =>
	{
		var group = await db.Groups.FirstAsync();
		var original = group.Description;
		group.Description = "caller pending";
		var cleaned = false; var rollbackCleanup = false;
		await Assert.ThrowsAsync<InvalidOperationException>(() => c.ExecuteAsync(async ct =>
		{
			c.AfterCommit(() => { cleaned = true; return Task.CompletedTask; });
			c.AfterRollback(() => { rollbackCleanup = true; return Task.CompletedTask; });
			await db.SaveChangesAsync(ct);
			await c.ExecuteAsync(_ => Task.FromResult(false), v => v, ct);
			return true;
		}, v => v));
		Assert.False(cleaned); Assert.True(rollbackCleanup);
		Assert.NotEmpty(db.ChangeTracker.Entries());
		await Assert.ThrowsAsync<InvalidOperationException>(() => c.ExecuteAsync(_ => Task.FromResult(true), v => v));
		await using var verify = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		Assert.Equal(original, (await verify.Groups.FindAsync(group.Id))!.Description);
	});
	[PostgresFact]
	public Task Unmanaged_transaction_is_rejected_before_delegate_runs() => Run(async (db, inbox, c) =>
	{
		await using var tx = await db.Database.BeginTransactionAsync();
		var ran = false;
		await Assert.ThrowsAsync<InvalidOperationException>(() => c.ExecuteAsync(_ => { ran = true; return Task.FromResult(true); }, v => v));
		Assert.False(ran);
		Assert.Same(tx, db.Database.CurrentTransaction);
	});
	[PostgresFact]
	public Task Cleanup_exception_does_not_change_committed_success() => Run(async (db, inbox, c) =>
	{
		var group = await db.Groups.FirstAsync();
		Assert.True(await c.ExecuteAsync(async ct =>
		{
			group.Description = "retained";
			await db.SaveChangesAsync(ct);
			c.AfterCommit(() => throw new IOException("synthetic cleanup failure"));
			return true;
		}, v => v));
		await using var verify = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		Assert.Equal("retained", (await verify.Groups.FindAsync(group.Id))!.Description);
	});
}
