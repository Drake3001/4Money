using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.DTOs.Groups;
using ZPI4M.Grouping.Services.Groups;
using ZPI4M.Shared.Storage;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class GroupArchiveTests : PostgresFixture
{
	[PostgresFact]
	public async Task Membership_sorting_uses_actor_join_date_and_stable_pagination()
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		await db.GroupMembers.Where(m => m.FkUserId == Anna).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsMember, false));
		var ids = Enumerable.Range(1, 6).Select(i => Guid.Parse($"d0000000-0000-0000-0000-{i:000000000000}")).ToArray();
		var joined = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		for (var i = 0; i < ids.Length; i++)
		{
			var group = new ZPI4M.Grouping.Models.Group
			{
				Id = ids[i],
				Name = new[] { "Zulu", "Alpha", "Beta", "Former", "Archived", "Outsider" }[i],
				CurrencyCode = "PLN",
				AdminUserId = Anna,
				IsArchived = i == 4
			};
			group.GroupMembers.Add(new ZPI4M.Grouping.Models.GroupMember
			{
				FkGroupId = ids[i],
				FkUserId = i == 5 ? Bartek : Anna,
				IsMember = i != 3,
				JoinedAt = i == 0 ? joined.AddDays(2) : joined
			});
			db.Groups.Add(group);
		}
		await db.SaveChangesAsync();
		var repository = new ZPI4M.Grouping.Repositories.GroupRepository.GroupRepository(db);
		var query = new ZPI4M.Grouping.Repositories.GroupRepository.GroupMembershipQuery
		{
			IsMember = true,
			PageSize = 2,
			SortBy = ZPI4M.Grouping.Repositories.GroupRepository.GroupMembershipSortBy.JoinedAt
		};
		var first = await repository.GetMembershipPageAsync(Anna, query);
		Assert.Equal(3, first.TotalCount);
		Assert.Equal(new[] { ids[1], ids[2] }, first.Items.Select(x => x.Group.Id));
		query.PageNumber = 2;
		Assert.Equal(ids[0], Assert.Single((await repository.GetMembershipPageAsync(Anna, query)).Items).Group.Id);
		query.PageNumber = 1;
		query.Descending = true;
		Assert.Equal(new[] { ids[0], ids[1] }, (await repository.GetMembershipPageAsync(Anna, query)).Items.Select(x => x.Group.Id));
		query.SortBy = ZPI4M.Grouping.Repositories.GroupRepository.GroupMembershipSortBy.Name;
		query.Descending = false;
		Assert.Equal(new[] { ids[1], ids[2] }, (await repository.GetMembershipPageAsync(Anna, query)).Items.Select(x => x.Group.Id));
		query.Descending = true;
		Assert.Equal(new[] { ids[0], ids[2] }, (await repository.GetMembershipPageAsync(Anna, query)).Items.Select(x => x.Group.Id));
	}

	private static readonly Guid Anna = Guid.Parse("10000000-0000-0000-0000-000000000001");
	private static readonly Guid Bartek = Guid.Parse("10000000-0000-0000-0000-000000000002");
	private static readonly Guid Group = Guid.Parse("20000000-0000-0000-0000-000000000001");
	[PostgresFact]
	public async Task Archive_is_idempotent_hides_reads_and_preserves_members()
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(model.Database.GetConnectionString()!, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		await using var inbox = new ZPI4M.Notifications.Database.NotificationsDbContext(new DbContextOptionsBuilder<ZPI4M.Notifications.Database.NotificationsDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options);
		var coordinator = new ZPI4M.Grouping.Services.Transactions.GroupingTransactionCoordinator(db, inbox, Microsoft.Extensions.Logging.Abstractions.NullLogger<ZPI4M.Grouping.Services.Transactions.GroupingTransactionCoordinator>.Instance);
		var service = new GroupService(db, new Users(), new Icons(), coordinator, new ZPI4M.Grouping.Repositories.GroupRepository.GroupRepository(db), new ZPI4M.Grouping.Repositories.GroupMemberRepository.GroupMemberRepository(db));

		Assert.Equal(204, (await service.ArchiveAsync(Anna, Group)).Status);
		Assert.Equal(204, (await service.ArchiveAsync(Anna, Group)).Status);
		Assert.Equal(404, (await service.GetAsync(Anna, Group)).Status);
		Assert.DoesNotContain((await service.GetPageAsync(Anna, new GroupListRequest())).Value!.Items, g => g.Id == Group);
		Assert.Equal(404, (await service.UpdateAsync(Anna, Group, new UpdateGroupRequest { Name = "Hidden" })).Status);
		Assert.Equal(2, await db.GroupMembers.CountAsync(m => m.FkGroupId == Group));
	}
	[PostgresFact]
	public async Task Migration_roundtrip_preserves_existing_group_and_unicode_description()
	{
		await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		var migrations = db.Database.GetMigrations().ToArray();
		var migrator = db.GetService<IMigrator>();
		await migrator.MigrateAsync(migrations[^2]);
		// Seed requires the newest fields, so insert synthetic data after applying the new migration.
		await migrator.MigrateAsync();
		await SeedScript.ExecuteAsync(db.Database.GetConnectionString()!, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await db.Database.ExecuteSqlRawAsync("UPDATE grouping.\"group\" SET description = repeat('😀',255)");
		await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE grouping.\"group\" SET description = repeat('a',256)"));
		await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE grouping.\"group\" SET icon_storage_key = 'custom'"));
		await db.Database.ExecuteSqlRawAsync("DELETE FROM limits.user_limit; DELETE FROM limits.group_limit; DELETE FROM expense.expense_share; DELETE FROM expense.expense_item; UPDATE notifications.notification SET fk_expense_id=NULL; DELETE FROM expense.expense;");
		await migrator.MigrateAsync(migrations[^2]);
		await migrator.MigrateAsync();
		Assert.False(db.Database.HasPendingModelChanges());
	}
	[PostgresFact]
	public async Task Membership_queries_filter_page_and_keep_reads_untracked()
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		var groups = new ZPI4M.Grouping.Repositories.GroupRepository.GroupRepository(db);
		var members = new ZPI4M.Grouping.Repositories.GroupMemberRepository.GroupMemberRepository(db);
		var query = new ZPI4M.Grouping.Repositories.GroupRepository.GroupMembershipQuery { IsMember = true, PageSize = 1 };
		var page = await groups.GetMembershipPageAsync(Anna, query);
		Assert.NotEmpty(page.Items);
		Assert.True(page.TotalCount >= page.Items.Count);
		Assert.All(page.Items, item => Assert.True(item.IsMember));
		Assert.NotNull(await groups.GetForActiveMemberAsync(Group, Anna));
		Assert.Empty(db.ChangeTracker.Entries());
		Assert.Equal(new[] { Bartek }, await members.GetActiveUserIdsAsync(Group, Anna));
		var member = await members.GetAsync(Group, Bartek);
		member!.IsMember = false;
		await db.SaveChangesAsync();
		Assert.Null(await groups.GetForActiveMemberAsync(Group, Bartek));
		Assert.DoesNotContain((await groups.GetMembershipPageAsync(Bartek, new() { IsMember = true })).Items, item => item.Group.Id == Group);
		Assert.Contains((await groups.GetMembershipPageAsync(Bartek, new() { IsMember = false })).Items, item => item.Group.Id == Group && !item.IsMember);
		Assert.Contains((await groups.GetMembershipPageAsync(Bartek, new())).Items, item => item.Group.Id == Group);
	}

	private sealed class Users : IUserLookupService
	{
		public Task<IReadOnlyCollection<Guid>> GetActiveUserIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => Task.FromResult(ids);
	}
	private sealed class Icons : IGroupIconService
	{
		public Task<IReadOnlyList<GroupIconPreset>> GetPresetsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<GroupIconPreset>>([]);
		public Task<bool> IsPresetAsync(string key, CancellationToken ct) => Task.FromResult(true);
		public Task<string> UploadAsync(Guid id, Stream content, string contentType, CancellationToken ct) => Task.FromResult("test");
		public Task<FileUrlResult> GetUrlAsync(string key, CancellationToken ct) => throw new NotSupportedException();
		public Task CleanupAsync(string key) => Task.CompletedTask;
	}
}
