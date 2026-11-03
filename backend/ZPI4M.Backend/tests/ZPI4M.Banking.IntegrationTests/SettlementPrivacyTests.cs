using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories.BankAccountRepository;
using ZPI4M.Banking.Repositories.BankTransactionRepository;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Repositories.GroupSettlementRepository;
using ZPI4M.Grouping.Services.Settlements;
using ZPI4M.User.Database;
using ZPI4M.User.Services.BankingProfile;
namespace ZPI4M.Banking.IntegrationTests;

public sealed class SettlementPrivacyTests : PostgresFixture
{
	[PostgresFact]
	public async Task Real_banking_lookup_hides_identifier_from_group_administrator()
	{
		await using var model = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		await model.Database.MigrateAsync();
		await SeedScript.ExecuteAsync(ConnectionString, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")));
		await using var db = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(ConnectionString).Options);
		await using var bank = new BankingDbContext(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(ConnectionString).Options);
		await using var user = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		var lookup = new BankingReadService(new BankAccountRepository(bank), new BankTransactionRepository(bank), new UserBankingProfileService(user));
		var service = new GroupSettlementService(db, new GroupRepository(db), new GroupMemberRepository(db), new GroupSettlementRepository(db), null!, null!, TimeProvider.System, lookup, null!);
		var group = Guid.Parse("20000000-0000-0000-0000-000000000001");
		var admin = Guid.Parse("10000000-0000-0000-0000-000000000001");
		var owner = Guid.Parse("10000000-0000-0000-0000-000000000002");
		var linked = await db.GroupSettlements.AsNoTracking().SingleAsync(s => s.GroupId == group && s.BankTransactionId != null);
		Assert.Null((await service.GetAsync(admin, group, linked.Id)).Value!.BankTransactionId);
		Assert.Equal(linked.BankTransactionId, (await service.GetAsync(owner, group, linked.Id)).Value!.BankTransactionId);
		Assert.Null((await service.ListAsync(admin, group, new())).Value!.Items.Single(s => s.Id == linked.Id).BankTransactionId);
		Assert.Equal(linked.BankTransactionId, (await service.ListAsync(owner, group, new())).Value!.Items.Single(s => s.Id == linked.Id).BankTransactionId);
		Assert.Equal(404, (await service.GetAsync(Guid.NewGuid(), group, linked.Id)).Status);
		Assert.Empty(db.ChangeTracker.Entries());
		Assert.Equal(linked.BankTransactionId, (await db.GroupSettlements.AsNoTracking().SingleAsync(s => s.Id == linked.Id)).BankTransactionId);
	}
}
