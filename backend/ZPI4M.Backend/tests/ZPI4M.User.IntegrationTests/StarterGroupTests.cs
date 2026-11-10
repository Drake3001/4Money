using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.Grouping.Models;

namespace ZPI4M.User.IntegrationTests;

public sealed class StarterGroupTests : PostgresFixture
{
	[PostgresFact]
	public async Task RegistrationCreatesOneOrdinaryGroupBeforeEmailConfirmation()
	{
		await using var host = await StartHostAsync();
		using var registered = await host.Client.PostAsJsonAsync("/api/users/register", new
		{
			login = "starter.user",
			email = "starter@example.test",
			name = "Starter",
			surname = "User",
			password = Password,
			birthDate = "2000-01-01"
		});
		Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
		var userId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
		await using var context = OpenModel();
		var group = await context.Set<Group>().Include(g => g.GroupMembers).SingleAsync(g => g.AdminUserId == userId);
		Assert.Equal("Wydatki osobiste", group.Name);
		Assert.Equal("PLN", group.CurrencyCode);
		Assert.False(group.IsArchived);
		Assert.Null(group.Icon);
		Assert.Null(group.IconStorageKey);
		Assert.Null(group.Description);
		var member = Assert.Single(group.GroupMembers);
		Assert.Equal(userId, member.FkUserId);
		Assert.True(member.IsMember);
		Assert.Equal(DateTimeKind.Utc, member.JoinedAt.Kind);
		Assert.True(member.JoinedAt > DateTime.UtcNow.AddMinutes(-5));
		Assert.Equal(0m, member.TotalPaid);
		Assert.Equal(0m, member.TotalShare);
		Assert.Equal(0m, member.TotalSettlementsSent);
		Assert.Equal(0m, member.TotalSettlementsReceived);

		using var confirmed = await host.Client.PostAsJsonAsync("/api/users/confirm-email", new { token = Email.LastToken });
		Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
		Assert.Equal(1, await context.Set<Group>().CountAsync(g => g.AdminUserId == userId));

		// A starter group uses exactly the same model as groups created by the user.
		group.Name = "Wspólny budżet";
		group.GroupMembers.Add(new GroupMember { FkUserId = AnnaId, FkGroupId = group.Id, JoinedAt = DateTime.UtcNow });
		await context.SaveChangesAsync();
		Assert.Equal(2, await context.Set<GroupMember>().CountAsync(m => m.FkGroupId == group.Id));
	}

	[PostgresFact]
	public async Task MembershipFailureRollsBackRegistrationGroupAndTokenWithoutSendingEmail()
	{
		await using var context = OpenModel();
		var groupCount = await context.Set<Group>().CountAsync();
		var memberCount = await context.Set<GroupMember>().CountAsync();
		await context.Database.ExecuteSqlRawAsync("""
			ALTER TABLE grouping.group_user ADD CONSTRAINT test_reject_starter_membership
			CHECK (total_paid > 0) NOT VALID
			""");
		await using var host = await StartHostAsync();
		var error = await Assert.ThrowsAsync<DbUpdateException>(() => host.Client.PostAsJsonAsync("/api/users/register", new
		{
			login = "failed.starter",
			email = "failed@example.test",
			name = "Failed",
			surname = "Starter",
			password = Password,
			birthDate = "2000-01-01"
		}));
		Assert.Equal("test_reject_starter_membership", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
		await using var users = OpenDatabase();
		Assert.False(await users.Users.AnyAsync(u => u.Login == "failed.starter"));
		Assert.Empty(await users.UserTokens.ToListAsync());
		Assert.Equal(groupCount, await context.Set<Group>().CountAsync());
		Assert.Equal(memberCount, await context.Set<GroupMember>().CountAsync());
		Assert.Empty(Email.Messages);
	}

	[PostgresFact]
	public async Task OuterTransactionRollbackRemovesUserTokenGroupAndMembership()
	{
		var user = NewUser();
		user.UserTokens.Add(new Models.UserToken
		{
			Id = Guid.NewGuid(),
			FkUserId = user.Id,
			TokenHash = "synthetic-confirmation-hash",
			Purpose = Models.UserTokenPurpose.EmailConfirmation,
			TargetEmail = user.Email,
			CreatedAt = DateTime.UtcNow,
			ExpiresAt = DateTime.UtcNow.AddHours(1)
		});
		await using (var users = OpenDatabase())
		{
			await using var transaction = await users.Database.BeginTransactionAsync();
			users.Users.Add(user);
			await users.SaveChangesAsync();
			Assert.Equal(1, await users.UserTokens.CountAsync(t => t.FkUserId == user.Id));
			Assert.Equal(1, await users.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM grouping.\"group\" WHERE admin_user_id = {user.Id}").SingleAsync());
			await transaction.RollbackAsync();
		}
		await using var context = OpenModel();
		Assert.False(await context.Set<Models.User>().AnyAsync(u => u.Id == user.Id));
		Assert.False(await context.Set<Models.UserToken>().AnyAsync(t => t.FkUserId == user.Id));
		Assert.False(await context.Set<Group>().AnyAsync(g => g.AdminUserId == user.Id));
		Assert.False(await context.Set<GroupMember>().AnyAsync(m => m.FkUserId == user.Id));
	}

	[PostgresFact]
	public async Task MigrationDoesNotBackfillAndDownKeepsGroupsAndDisablesAutomaticCreation()
	{
		await using var context = OpenModel();
		var migrations = context.Database.GetMigrations().ToArray();
		var triggerIndex = Array.FindIndex(migrations, m => m.EndsWith("_UserImprovements", StringComparison.Ordinal));
		var migrator = context.GetService<IMigrator>();
		var groupCount = await context.Set<Group>().CountAsync();
		await migrator.MigrateAsync(migrations[triggerIndex - 1]);
		Assert.Equal(groupCount, await context.Set<Group>().CountAsync());
		var existingUser = NewUser();
		// This schema predates GoogleSubject; the current EF model cannot insert into it.
		await context.Database.ExecuteSqlInterpolatedAsync($"""
			INSERT INTO "user"."user" (_id, login, name, surname, email, password_hash, birth_date, is_active)
			VALUES ({existingUser.Id}, {existingUser.Login}, {existingUser.Name}, {existingUser.Surname},
				{existingUser.Email}, {existingUser.PasswordHash}, {existingUser.BirthDate}, {existingUser.IsActive})
			""");
		Assert.False(await context.Set<Group>().AnyAsync(g => g.AdminUserId == existingUser.Id));
		await migrator.MigrateAsync();
		Assert.False(await context.Set<Group>().AnyAsync(g => g.AdminUserId == existingUser.Id));
		Assert.Equal(groupCount, await context.Set<Group>().CountAsync());
		var newUser = NewUser();
		context.Set<Models.User>().Add(newUser);
		await context.SaveChangesAsync();
		Assert.Equal(groupCount + 1, await context.Set<Group>().CountAsync());
		await migrator.MigrateAsync(migrations[triggerIndex - 1]);
		Assert.True(await context.Set<Group>().AnyAsync(g => g.AdminUserId == newUser.Id));
		Assert.True(await context.Set<GroupMember>().AnyAsync(m => m.FkUserId == newUser.Id));
		Assert.Equal(1, await context.Set<Models.User>().CountAsync(u => u.Email == existingUser.Email));
		await migrator.MigrateAsync();
		Assert.Equal(groupCount + 1, await context.Set<Group>().CountAsync());
		Assert.False(context.Database.HasPendingModelChanges());
	}

	[PostgresFact]
	public async Task SeedCanBeRepeatedAndCreatesOneStarterGroupPerInsertedUser()
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using var seed = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")), connection);
		await seed.ExecuteNonQueryAsync();
		await using var context = OpenModel();
		Assert.Equal(7, await context.Set<Group>().CountAsync());
		var starterGroups = await context.Set<Group>().Where(g => g.Name == "Wydatki osobiste").Include(g => g.GroupMembers).ToListAsync();
		Assert.Equal(5, starterGroups.Count);
		Assert.Equal(5, starterGroups.Select(g => g.AdminUserId).Distinct().Count());
		Assert.All(starterGroups, g => Assert.Equal(g.AdminUserId, Assert.Single(g.GroupMembers).FkUserId));
	}

	private AppDbContext OpenModel() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

	private static Models.User NewUser()
	{
		var id = Guid.NewGuid();
		return new Models.User
		{
			Id = id,
			Login = $"starter-{id:N}",
			Email = $"starter-{id:N}@example.test",
			Name = "Starter",
			Surname = "User",
			PasswordHash = "synthetic-unused-hash",
			BirthDate = new DateOnly(2000, 1, 1),
			CreatedAt = DateTime.UtcNow,
			IsActive = true
		};
	}
}
