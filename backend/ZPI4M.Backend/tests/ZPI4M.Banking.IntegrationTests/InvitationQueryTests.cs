using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupInvitationRepository;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class InvitationQueryTests : PostgresFixture
{
	[PostgresFact]
	public async Task Reads_are_group_scoped_stable_filtered_and_untracked()
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(model.Database.GetConnectionString()!, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		var group = Guid.Parse("20000000-0000-0000-0000-000000000002");
		var author = Guid.Parse("10000000-0000-0000-0000-000000000002");
		var expires = DateTime.UtcNow.AddDays(2);
		var first = new GroupInvitation { Id = Guid.Parse("d0000000-0000-0000-0000-000000000001"), GroupId = group, CreatedByUserId = author, TokenHash = "synthetic-query-1", MaxUses = 1, ExpiresAt = expires };
		var second = new GroupInvitation { Id = Guid.Parse("d0000000-0000-0000-0000-000000000002"), GroupId = group, CreatedByUserId = author, TokenHash = "synthetic-query-2", MaxUses = 1, ExpiresAt = expires, IsActive = false };
		db.GroupInvitations.AddRange(first, second);
		await db.SaveChangesAsync();
		db.ChangeTracker.Clear();
		var repo = new GroupInvitationRepository(db);
		var page = await repo.GetPageAsync(group, new GroupInvitationQuery { PageSize = 1 });
		Assert.Equal(2, page.TotalCount);
		Assert.Equal(first.Id, Assert.Single(page.Items).Id);
		Assert.Equal(second.Id, Assert.Single((await repo.GetPageAsync(group, new GroupInvitationQuery { PageSize = 1, PageNumber = 2 })).Items).Id);
		Assert.Equal(first.Id, Assert.Single((await repo.GetPageAsync(group, new GroupInvitationQuery { IsActive = true })).Items).Id);
		Assert.Equal(second.Id, Assert.Single((await repo.GetPageAsync(group, new GroupInvitationQuery { IsActive = false })).Items).Id);
		Assert.Equal(first.Id, Assert.Single((await repo.GetPageAsync(group, new GroupInvitationQuery { Descending = true, PageSize = 1 })).Items).Id);
		Assert.Equal(first.Id, (await repo.GetByTokenHashAsync(first.TokenHash))!.Id);
		Assert.Null(await repo.GetByTokenHashAsync("missing-synthetic-hash"));
		Assert.Empty((await repo.GetPageAsync(Guid.NewGuid(), new GroupInvitationQuery())).Items);
		Assert.Empty(db.ChangeTracker.Entries());
	}
}
