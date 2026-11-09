using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.User.Database;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class ProvisioningFlowTests : PostgresFixture
{
	[PostgresFact]
	public async Task ConcurrentConnectionStartsShareOnePersistedMapping()
	{
		var user = await AddUserAsync();
		await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
		{
			using var session = OpenSession();
			Assert.True((await session.Service.StartConnectionAsync(user)).IsSuccess);
		}));
		using (var check = OpenSession())
		{
			Assert.Equal(5, await check.Database.ConnectionAttempts.CountAsync());
			Assert.Empty(await check.Database.Connections.ToListAsync());
			Assert.Empty(await check.Database.Refreshes.ToListAsync());
		}
		await using var database = Users();
		Assert.Equal($"tink-{user:N}", (await database.Users.SingleAsync(x => x.Id == user)).TinkUserId);
		Assert.Equal(1, Tink.CreateCalls);
	}

	[PostgresFact]
	public async Task DeactivationDuringRemoteCallCannotPersistMappingOrReturnSuccess()
	{
		var user = await AddUserAsync();
		Tink.BeforeCreate = async () =>
		{
			await using var database = Users();
			await database.Users.Where(x => x.Id == user).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
		};
		using var session = OpenSession();
		Assert.Equal(BankingErrorCode.Forbidden, (await session.Service.StartConnectionAsync(user)).Error);
		await using var check = Users();
		Assert.Null((await check.Users.SingleAsync(x => x.Id == user)).TinkUserId);
	}

	[PostgresFact]
	public async Task RetryRecoversRemoteIdentityAfterLocalWriteFails()
	{
		var user = await AddUserAsync();
		// Simulate a local failure after the remote identity has been created.
		await using (var database = Users())
			await database.Database.ExecuteSqlRawAsync("""
				CREATE FUNCTION "user".reject_tink_mapping() RETURNS trigger LANGUAGE plpgsql AS $$
				BEGIN RAISE EXCEPTION 'synthetic persistence failure'; END $$;
				CREATE TRIGGER reject_tink_mapping BEFORE UPDATE OF tink_user_id ON "user"."user"
				FOR EACH ROW EXECUTE FUNCTION "user".reject_tink_mapping();
				""");
		using (var failed = OpenSession())
			await Assert.ThrowsAsync<PostgresException>(() => failed.Service.StartConnectionAsync(user));
		await using (var database = Users())
		{
			Assert.Null((await database.Users.SingleAsync(x => x.Id == user)).TinkUserId);
			await database.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_tink_mapping ON \"user\".\"user\";");
		}
		using var retry = OpenSession();
		// FakeTink returns the same identity for a stable local ID, modelling create-or-recover.
		Assert.True((await retry.Service.StartConnectionAsync(user)).IsSuccess);
		await using var check = Users();
		Assert.Equal($"tink-{user:N}", (await check.Users.SingleAsync(x => x.Id == user)).TinkUserId);
		Assert.Equal(2, Tink.CreateCalls);
	}

	private UserDbContext Users() => new(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
}
