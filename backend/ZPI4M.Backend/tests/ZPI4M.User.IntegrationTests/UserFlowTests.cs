using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.Models;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.User.IntegrationTests;

public sealed class UserFlowTests : PostgresFixture
{
	[PostgresTheory]
	[InlineData("seed_anna", 200)]
	[InlineData("seed_bartek", 200)]
	[InlineData("seed_celina", 200)]
	[InlineData("postman.banking", 200)]
	[InlineData("seed_dawid", 403)]
	public async Task SeedPasswordsWorkWithoutOverridingHashes(string login, int expectedStatus)
	{
		await using var host = await StartHostAsync();
		await PostAsync(host.Client, "login", new { login, password = Password }, (HttpStatusCode)expectedStatus);
	}

	[PostgresFact]
	public async Task SeedCanResetBankingConnectionsAndTinkMapping()
	{
		await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using (var command = new Npgsql.NpgsqlCommand("""
			INSERT INTO banking.bank_refresh
			(_id, four_money_user_id, tink_user_id, status, created_at, next_attempt_at, attempts, discovery_completed)
			VALUES ('13000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000010',
			'seed-tink-user', 'QUEUED', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0, false);
			UPDATE "user"."user" SET tink_user_id = 'seed-tink-user'
			WHERE _id = '10000000-0000-0000-0000-000000000010';
			INSERT INTO banking.bank_connection
			(_id, four_money_user_id, tink_user_id, credentials_id, connection_status, sync_status, created_at, connected_at)
			VALUES ('11000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000010',
			'seed-tink-user', 'seed-credential', 'Connected', 'NotStarted', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
			INSERT INTO banking.bank_connection_attempt
			(_id, four_money_user_id, status, correlation_state_hash, created_at, expires_at, bank_connection_id, version)
			VALUES ('12000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000010',
			'Completed', 'synthetic-state-hash', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP + INTERVAL '15 minutes',
			'11000000-0000-0000-0000-000000000001', 0);
			""", connection))
			await command.ExecuteNonQueryAsync();
		await using (var reset = new Npgsql.NpgsqlCommand(await File.ReadAllTextAsync(
			Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql")), connection))
			await reset.ExecuteNonQueryAsync();
		await using var context = OpenDatabase();
		Assert.Equal(5, await context.Users.CountAsync());
		Assert.False(await context.Users.AnyAsync(user => user.TinkUserId != null));
		await using var check = new Npgsql.NpgsqlCommand(
			"SELECT (SELECT COUNT(*) FROM banking.bank_connection) + (SELECT COUNT(*) FROM banking.bank_connection_attempt) + (SELECT COUNT(*) FROM banking.bank_refresh) + (SELECT COUNT(*) FROM banking.bank_refresh_account)", connection);
		Assert.Equal(0L, await check.ExecuteScalarAsync());
	}

	[PostgresFact]
	public async Task BaselineMigrationsAndSeedProduceUsableDatabase()
	{
		await using var context = OpenDatabase();
		Assert.Equal(5, await context.Users.CountAsync());
		Assert.Equal(4, await context.Users.CountAsync(u => u.IsActive && u.IsEmailConfirmed));
		Assert.Equal("anna@example.test", (await context.Users.SingleAsync(u => u.Id == AnnaId)).Email);
		Assert.Empty(await context.UserTokens.ToListAsync());
		await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using var command = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM expense.expense", connection);
		Assert.Equal(4L, await command.ExecuteScalarAsync());
	}

	[PostgresFact]
	public async Task RegistrationStoresHashesAndConfirmationIsRequiredAndSingleUse()
	{
		await using var host = await StartHostAsync();
		using var registered = await host.Client.PostAsJsonAsync("/api/users/register", Registration());
		Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
		var json = await registered.Content.ReadFromJsonAsync<JsonElement>();
		Assert.False(json.GetProperty("isEmailConfirmed").GetBoolean());
		Assert.Equal(new[] { "id", "login", "name", "surname", "email", "birthDate", "isEmailConfirmed", "createdAt" },
			json.EnumerateObject().Select(p => p.Name));
		Assert.Equal("new@example.test", Assert.Single(Email.Messages).Recipient);
		await using (var context = OpenDatabase())
		{
			var user = await context.Users.SingleAsync(u => u.Login == "new.user");
			Assert.NotEqual(Password, user.PasswordHash);
			Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<Models.User>().VerifyHashedPassword(user, user.PasswordHash, Password));
			var token = await context.UserTokens.SingleAsync(t => t.FkUserId == user.Id);
			Assert.Equal(new UserTokenGenerator().HashToken(Email.LastToken), token.TokenHash);
			Assert.Equal(UserTokenPurpose.EmailConfirmation, token.Purpose);
			Assert.Equal(user.Email, token.TargetEmail);
			Assert.True(token.ExpiresAt > token.CreatedAt);
		}
		await PostAsync(host.Client, "login", new { login = "new.user", password = Password }, HttpStatusCode.Unauthorized);
		var confirmation = new { token = Email.LastToken };
		await PostAsync(host.Client, "confirm-email", confirmation, HttpStatusCode.NoContent);
		await PostAsync(host.Client, "confirm-email", confirmation, HttpStatusCode.BadRequest);
		await LoginAsync(host.Client, "new.user");
	}

	[PostgresTheory]
	[InlineData("seed_anna", "new@example.test")]
	[InlineData("new.user", "anna@example.test")]
	public async Task DuplicateLoginOrEmailDoesNotCreateUserOrSendEmail(string login, string email)
	{
		await using var host = await StartHostAsync();
		await PostAsync(host.Client, "register", Registration(login, email), HttpStatusCode.Conflict);
		await using var context = OpenDatabase();
		Assert.Equal(5, await context.Users.CountAsync());
		Assert.Empty(Email.Messages);
		Assert.Empty(await context.UserTokens.ToListAsync());
	}

	[PostgresTheory]
	[InlineData("missing", "TestPassword1!", true, true, 401)]
	[InlineData("seed_anna", "WrongPassword1!", true, true, 401)]
	[InlineData("seed_anna", "TestPassword1!", false, true, 403)]
	[InlineData("seed_anna", "TestPassword1!", true, false, 401)]
	public async Task LoginRejectsUnavailableAccountsWithoutCreatingSession(string login, string password, bool active, bool confirmed, int status)
	{
		await SetPasswordAsync(AnnaId);
		await using (var context = OpenDatabase())
			await context.Users.Where(u => u.Id == AnnaId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, active).SetProperty(u => u.IsEmailConfirmed, confirmed));
		await using var host = await StartHostAsync();
		await PostAsync(host.Client, "login", new { login, password }, (HttpStatusCode)status);
		await using var database = OpenDatabase();
		Assert.Empty(await database.UserTokens.ToListAsync());
	}

	[PostgresFact]
	public async Task RefreshRotatesTokenPreservesAbsoluteExpiryAndLogoutRevokesAccess()
	{
		await SetPasswordAsync(AnnaId);
		await using var host = await StartHostAsync();
		var first = await LoginAsync(host.Client);
		Authenticate(host.Client, first);
		using (var profile = await host.Client.GetAsync("/api/users/profile"))
			Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
		var rotated = await RefreshAsync(host.Client, first.RefreshToken);
		Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);
		// PostgreSQL timestamps retain microseconds; DateTime also carries 100 ns ticks.
		Assert.Equal(first.RefreshTokenExpiresAt.Ticks / 10, rotated.RefreshTokenExpiresAt.Ticks / 10);
		Assert.Equal("Bearer", rotated.TokenType);
		await PostAsync(host.Client, "refresh", new { refreshToken = first.RefreshToken }, HttpStatusCode.Unauthorized);
		await using (var context = OpenDatabase())
		{
			var session = await context.UserTokens.SingleAsync(t => t.FkUserId == AnnaId);
			Assert.Equal(new UserTokenGenerator().HashToken(rotated.RefreshToken), session.TokenHash);
			Assert.Null(session.TargetEmail);
			Assert.NotNull((await context.Users.SingleAsync(u => u.Id == AnnaId)).LastLoginTime);
		}
		Authenticate(host.Client, rotated);
		await PostAsync(host.Client, "logout", new { }, HttpStatusCode.NoContent);
		using var denied = await host.Client.GetAsync("/api/users/profile");
		Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
		await PostAsync(host.Client, "refresh", new { refreshToken = rotated.RefreshToken }, HttpStatusCode.Unauthorized);
	}

	[PostgresFact]
	public async Task NewLoginInvalidatesPreviousSession()
	{
		await SetPasswordAsync(AnnaId);
		await using var host = await StartHostAsync();
		var old = await LoginAsync(host.Client);
		var current = await LoginAsync(host.Client);
		Authenticate(host.Client, old);
		using (var denied = await host.Client.GetAsync("/api/users/profile"))
			Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
		await PostAsync(host.Client, "refresh", new { refreshToken = old.RefreshToken }, HttpStatusCode.Unauthorized);
		Authenticate(host.Client, current);
		using var allowed = await host.Client.GetAsync("/api/users/profile");
		Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
	}

	[PostgresFact]
	public async Task ConcurrentRefreshConsumesTokenExactlyOnce()
	{
		await SetPasswordAsync(AnnaId);
		await using var host = await StartHostAsync();
		var login = await LoginAsync(host.Client);
		var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
			host.Client.PostAsJsonAsync("/api/users/refresh", new { refreshToken = login.RefreshToken })));
		try
		{
			Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
			Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
			await using var context = OpenDatabase();
			Assert.Single(await context.UserTokens.Where(t => t.FkUserId == AnnaId).ToListAsync());
		}
		finally
		{
			foreach (var response in responses)
				response.Dispose();
		}
	}

	[PostgresFact]
	public async Task PasswordResetIsSingleUseAndRevokesEveryExistingToken()
	{
		await SetPasswordAsync(AnnaId);
		await using var host = await StartHostAsync();
		var login = await LoginAsync(host.Client);
		await PostAsync(host.Client, "forgot-password", new { email = "anna@example.test" }, HttpStatusCode.Accepted);
		var token = Email.LastToken;
		await PostAsync(host.Client, "forgot-password", new { email = "anna@example.test" }, HttpStatusCode.Accepted);
		Assert.Single(Email.Messages);
		await PostAsync(host.Client, "confirm-email", new { token }, HttpStatusCode.BadRequest);
		var reset = new { token, newPassword = "NewPassword2!" };
		await PostAsync(host.Client, "reset-password", reset, HttpStatusCode.NoContent);
		await PostAsync(host.Client, "reset-password", reset, HttpStatusCode.BadRequest);
		Authenticate(host.Client, login);
		using (var denied = await host.Client.GetAsync("/api/users/profile"))
			Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
		await PostAsync(host.Client, "refresh", new { refreshToken = login.RefreshToken }, HttpStatusCode.Unauthorized);
		await PostAsync(host.Client, "login", new { login = "seed_anna", password = Password }, HttpStatusCode.Unauthorized);
		await LoginAsync(host.Client, password: "NewPassword2!");
	}

	[PostgresTheory]
	[InlineData("unknown@example.test")]
	[InlineData("dawid@example.test")]
	public async Task ForgotPasswordDoesNotRevealMissingOrInactiveAccount(string email)
	{
		await using var host = await StartHostAsync();
		await PostAsync(host.Client, "forgot-password", new { email }, HttpStatusCode.Accepted);
		Assert.Empty(Email.Messages);
		await using var context = OpenDatabase();
		Assert.Empty(await context.UserTokens.ToListAsync());
	}

	[PostgresFact]
	public async Task EmailChangeRequiresPasswordAndConfirmationThenRevokesSessions()
	{
		await SetPasswordAsync(AnnaId);
		await using var host = await StartHostAsync();
		var login = await LoginAsync(host.Client);
		Authenticate(host.Client, login);
		await PostAsync(host.Client, "profile/change-email", new { newEmail = "new@example.test", currentPassword = "Wrong1!" }, HttpStatusCode.BadRequest);
		await PostAsync(host.Client, "profile/change-email", new { newEmail = "bartek@example.test", currentPassword = Password }, HttpStatusCode.Conflict);
		Assert.Empty(Email.Messages);
		var change = new { newEmail = "changed@example.test", currentPassword = Password };
		await PostAsync(host.Client, "profile/change-email", change, HttpStatusCode.Accepted);
		Assert.Equal("changed@example.test", Assert.Single(Email.Messages).Recipient);
		var confirmation = new { token = Email.LastToken };
		await PostAsync(host.Client, "profile/change-email", change, HttpStatusCode.TooManyRequests);
		await using (var context = OpenDatabase())
			Assert.Equal("anna@example.test", (await context.Users.SingleAsync(u => u.Id == AnnaId)).Email);
		host.Client.DefaultRequestHeaders.Authorization = null;
		await PostAsync(host.Client, "confirm-email-change", confirmation, HttpStatusCode.NoContent);
		await PostAsync(host.Client, "confirm-email-change", confirmation, HttpStatusCode.BadRequest);
		Authenticate(host.Client, login);
		using var denied = await host.Client.GetAsync("/api/users/profile");
		Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
		await using var database = OpenDatabase();
		var changed = await database.Users.SingleAsync(u => u.Id == AnnaId);
		Assert.Equal("changed@example.test", changed.Email);
		Assert.True(changed.IsEmailConfirmed);
		Assert.Empty(await database.UserTokens.Where(t => t.FkUserId == AnnaId).ToListAsync());
	}

	[PostgresFact]
	public async Task EmailConflictAtConfirmationRollsBackTokenConsumption()
	{
		await SetPasswordAsync(AnnaId);
		await using var host = await StartHostAsync();
		Authenticate(host.Client, await LoginAsync(host.Client));
		await PostAsync(host.Client, "profile/change-email", new { newEmail = "claimed@example.test", currentPassword = Password }, HttpStatusCode.Accepted);
		var token = Email.LastToken;
		await using (var context = OpenDatabase())
			await context.Users.Where(u => u.Login == "seed_bartek").ExecuteUpdateAsync(s => s.SetProperty(u => u.Email, "claimed@example.test"));
		await PostAsync(host.Client, "confirm-email-change", new { token }, HttpStatusCode.Conflict);
		await using var database = OpenDatabase();
		Assert.Equal("anna@example.test", (await database.Users.SingleAsync(u => u.Id == AnnaId)).Email);
		Assert.True(await database.UserTokens.AnyAsync(t => t.TokenHash == new UserTokenGenerator().HashToken(token)));
	}

	[PostgresFact]
	public async Task DeactivationRequiresSingleUseConfirmationPreservesDataAndRevokesSessions()
	{
		await SetPasswordAsync(AnnaId);
		await using var host = await StartHostAsync();
		var login = await LoginAsync(host.Client);
		Authenticate(host.Client, login);
		await PostAsync(host.Client, "profile/deactivate", new { }, HttpStatusCode.Accepted);
		var confirmation = new { token = Email.LastToken };
		await PostAsync(host.Client, "profile/deactivate", new { }, HttpStatusCode.TooManyRequests);
		await using (var context = OpenDatabase())
			Assert.True((await context.Users.SingleAsync(u => u.Id == AnnaId)).IsActive);
		host.Client.DefaultRequestHeaders.Authorization = null;
		await PostAsync(host.Client, "confirm-deactivation", confirmation, HttpStatusCode.NoContent);
		await PostAsync(host.Client, "confirm-deactivation", confirmation, HttpStatusCode.BadRequest);
		Authenticate(host.Client, login);
		using (var denied = await host.Client.GetAsync("/api/users/profile"))
			Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
		await PostAsync(host.Client, "refresh", new { refreshToken = login.RefreshToken }, HttpStatusCode.Unauthorized);
		await PostAsync(host.Client, "login", new { login = "seed_anna", password = Password }, HttpStatusCode.Forbidden);
		await using var database = OpenDatabase();
		Assert.Equal(5, await database.Users.CountAsync());
		Assert.False((await database.Users.SingleAsync(u => u.Id == AnnaId)).IsActive);
		Assert.Empty(await database.UserTokens.Where(t => t.FkUserId == AnnaId).ToListAsync());
	}

	[PostgresTheory]
	[InlineData("confirm-email", UserTokenPurpose.EmailConfirmation, 400)]
	[InlineData("confirm-email-change", UserTokenPurpose.EmailChange, 400)]
	[InlineData("reset-password", UserTokenPurpose.PasswordReset, 400)]
	[InlineData("confirm-deactivation", UserTokenPurpose.AccountDeactivation, 400)]
	[InlineData("refresh", UserTokenPurpose.RefreshToken, 401)]
	public async Task ExpiredTokensCannotChangeAccountOrCreateSession(string route, UserTokenPurpose purpose, int status)
	{
		var raw = new UserTokenGenerator().GenerateToken();
		await using (var context = OpenDatabase())
		{
			if (purpose == UserTokenPurpose.EmailConfirmation)
				await context.Users.Where(u => u.Id == AnnaId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsEmailConfirmed, false));
			context.UserTokens.Add(new UserToken
			{
				Id = Guid.NewGuid(),
				FkUserId = AnnaId,
				Purpose = purpose,
				TokenHash = new UserTokenGenerator().HashToken(raw),
				TargetEmail = purpose == UserTokenPurpose.RefreshToken ? null : purpose == UserTokenPurpose.EmailChange ? "new@example.test" : "anna@example.test",
				CreatedAt = DateTime.UtcNow.AddDays(-2),
				ExpiresAt = DateTime.UtcNow.AddDays(-1)
			});
			await context.SaveChangesAsync();
		}
		await using var host = await StartHostAsync();
		await PostAsync(host.Client, route, new { token = raw, refreshToken = raw, newPassword = "NewPassword2!" }, (HttpStatusCode)status);
		await using var database = OpenDatabase();
		var user = await database.Users.SingleAsync(u => u.Id == AnnaId);
		Assert.True(user.IsActive);
		Assert.Equal("anna@example.test", user.Email);
		Assert.Equal(purpose != UserTokenPurpose.EmailConfirmation, user.IsEmailConfirmed);
		Assert.Single(await database.UserTokens.ToListAsync());
	}

	[PostgresFact]
	public async Task ResendRespectsCooldownAndRotatesOldConfirmationToken()
	{
		await using var host = await StartHostAsync();
		await PostAsync(host.Client, "register", Registration(), HttpStatusCode.Created);
		var old = Email.LastToken;
		await PostAsync(host.Client, "resend-confirmation", new { email = "new@example.test" }, HttpStatusCode.Accepted);
		Assert.Single(Email.Messages);
		await using (var context = OpenDatabase())
			await context.UserTokens.ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddMinutes(-2)));
		await PostAsync(host.Client, "resend-confirmation", new { email = "new@example.test" }, HttpStatusCode.Accepted);
		Assert.Equal(2, Email.Messages.Count);
		Assert.NotEqual(old, Email.LastToken);
		await PostAsync(host.Client, "confirm-email", new { token = old }, HttpStatusCode.BadRequest);
		await PostAsync(host.Client, "confirm-email", new { token = Email.LastToken }, HttpStatusCode.NoContent);
	}

	[PostgresFact]
	public async Task SmtpFailureKeepsRegisteredAccountAndDoesNotDeactivateExistingAccount()
	{
		await SetPasswordAsync(AnnaId);
		await using var host = await StartHostAsync();
		Email.Fail = true;
		await PostAsync(host.Client, "register", Registration(), HttpStatusCode.ServiceUnavailable);
		Authenticate(host.Client, await LoginAsync(host.Client));
		await PostAsync(host.Client, "profile/deactivate", new { }, HttpStatusCode.ServiceUnavailable);
		await PostAsync(host.Client, "forgot-password", new { email = "anna@example.test" }, HttpStatusCode.Accepted);
		await using var context = OpenDatabase();
		Assert.False((await context.Users.SingleAsync(u => u.Login == "new.user")).IsEmailConfirmed);
		Assert.True((await context.Users.SingleAsync(u => u.Id == AnnaId)).IsActive);
		Assert.True(await context.UserTokens.AnyAsync(t => t.FkUserId == AnnaId && t.Purpose == UserTokenPurpose.AccountDeactivation));
	}

	[PostgresFact]
	public async Task ProfileUpdateCannotOverrideEmailOrAnotherOwnerAndConflictsDoNotChangeData()
	{
		await SetPasswordAsync(AnnaId);
		await using var host = await StartHostAsync();
		Authenticate(host.Client, await LoginAsync(host.Client));
		using (var conflict = await host.Client.PutAsJsonAsync("/api/users/profile", Profile("seed_bartek")))
			Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
		using (var updated = await host.Client.PutAsJsonAsync("/api/users/profile?userId=10000000-0000-0000-0000-000000000002", Profile("updated.anna")))
			Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
		await using var context = OpenDatabase();
		var anna = await context.Users.SingleAsync(u => u.Id == AnnaId);
		Assert.Equal("updated.anna", anna.Login);
		Assert.Equal("Updated", anna.Name);
		Assert.Equal("anna@example.test", anna.Email);
		Assert.Null(anna.Phone);
		Assert.Null(anna.Icon);
		Assert.True(await context.Users.AnyAsync(u => u.Login == "seed_bartek"));
	}

	private static object Registration(string login = "new.user", string email = "new@example.test") => new
	{
		login,
		email,
		name = "New",
		surname = "User",
		password = Password,
		birthDate = "2000-01-01"
	};
	private static object Profile(string login) => new
	{
		login,
		name = "Updated",
		surname = "User",
		birthDate = "2000-01-01",
		phone = " ",
		icon = " ",
		email = "override@example.test"
	};
	private static void Authenticate(HttpClient client, AuthenticationResponse response) =>
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.AccessToken);

	private static async Task<AuthenticationResponse> LoginAsync(HttpClient client, string login = "seed_anna", string password = Password)
	{
		using var response = await client.PostAsJsonAsync("/api/users/login", new { login, password });
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
	}
	private static async Task<AuthenticationResponse> RefreshAsync(HttpClient client, string refreshToken)
	{
		using var response = await client.PostAsJsonAsync("/api/users/refresh", new { refreshToken });
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
	}
	private static async Task PostAsync(HttpClient client, string route, object body, HttpStatusCode expected)
	{
		using var response = await client.PostAsJsonAsync("/api/users/" + route, body);
		Assert.True(response.StatusCode == expected, $"{route}: expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
	}
}
