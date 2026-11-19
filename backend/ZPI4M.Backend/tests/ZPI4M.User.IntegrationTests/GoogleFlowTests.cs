using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.DTOs.Registration;
using ZPI4M.User.Integrations.Google;
using ZPI4M.User.Models;

namespace ZPI4M.User.IntegrationTests;

public sealed class GoogleFlowTests : PostgresFixture
{
	private readonly FakeGoogleValidator _google = new();
	private static GoogleIdentity Identity(string email = "google.user@gmail.com", string subject = "google-123", bool verified = true, string? domain = null) =>
		new(subject, email, verified, "Anna", "Testowa", domain);

	[PostgresFact]
	public async Task RegistrationFirstPasswordAndBothLoginMethodsShareSessionRules()
	{
		await using var host = await StartHostAsync(_google);
		var challenge = await ChallengeAsync(host.Client, Identity());
		await SendAsync(host.Client, "google/login", new { idToken = challenge.IdToken }, HttpStatusCode.Conflict, challenge.Cookie);
		using var registration = await SendAsync(host.Client, "google/register", Registration(challenge.IdToken), HttpStatusCode.Created, challenge.Cookie);
		var created = (await registration.Content.ReadFromJsonAsync<GoogleRegistrationResponse>())!;
		Assert.False(created.RequiresEmailConfirmation);
		Assert.NotNull(created.Authentication);
		Assert.Empty(Email.Messages);
		await using (var db = OpenDatabase())
		{
			var user = await db.Users.SingleAsync(u => u.Id == created.User.Id);
			Assert.Null(user.PasswordHash);
			Assert.Equal("google-123", user.GoogleSubject);
			Assert.True(user.IsEmailConfirmed);
			Assert.NotNull(user.LastLoginTime);
			Assert.Single(await db.UserTokens.Where(t => t.FkUserId == user.Id).ToListAsync());
		}
		await using (var connection = new NpgsqlConnection(ConnectionString))
		{
			await connection.OpenAsync();
			await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM grouping.group_user WHERE user_id = @id", connection);
			command.Parameters.AddWithValue("id", created.User.Id);
			Assert.Equal(1L, await command.ExecuteScalarAsync());
		}
		await SendAsync(host.Client, "google/login", new { idToken = challenge.IdToken }, HttpStatusCode.Unauthorized, challenge.Cookie);
		await SendAsync(host.Client, "login", new { login = "google.user", password = Password }, HttpStatusCode.Unauthorized);
		host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Authentication.AccessToken);
		using (var profile = await host.Client.GetAsync("/api/users/profile"))
		{
			Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
			var json = await profile.Content.ReadFromJsonAsync<JsonElement>();
			Assert.False(json.GetProperty("hasPassword").GetBoolean());
			Assert.True(json.GetProperty("isGoogleLinked").GetBoolean());
			Assert.False(json.TryGetProperty("googleSubject", out _));
		}
		await SendAsync(host.Client, "forgot-password", new { email = created.User.Email }, HttpStatusCode.Accepted);
		Assert.Contains("pierwszego", Assert.Single(Email.Messages).Body);
		var setupToken = Email.LastToken;
		await SendAsync(host.Client, "reset-password", new { token = setupToken, newPassword = Password }, HttpStatusCode.NoContent);
		await SendAsync(host.Client, "reset-password", new { token = setupToken, newPassword = Password }, HttpStatusCode.BadRequest);
		using (var expiredSession = await host.Client.GetAsync("/api/users/profile"))
			Assert.Equal(HttpStatusCode.Unauthorized, expiredSession.StatusCode);
		await SendAsync(host.Client, "refresh", new { refreshToken = created.Authentication.RefreshToken }, HttpStatusCode.Unauthorized);
		host.Client.DefaultRequestHeaders.Authorization = null;
		using var passwordLogin = await SendAsync(host.Client, "login", new { login = created.User.Email, password = Password }, HttpStatusCode.OK);
		var localSession = (await passwordLogin.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
		var next = await ChallengeAsync(host.Client, Identity(email: "changed.at.google@gmail.com"));
		using var googleLogin = await SendAsync(host.Client, "google/login", new { idToken = next.IdToken }, HttpStatusCode.OK, next.Cookie);
		var googleSession = (await googleLogin.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
		await SendAsync(host.Client, "refresh", new { refreshToken = localSession.RefreshToken }, HttpStatusCode.Unauthorized);
		host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", googleSession.AccessToken);
		using (var profile = await host.Client.GetAsync("/api/users/profile"))
		{
			var json = await profile.Content.ReadFromJsonAsync<JsonElement>();
			Assert.True(json.GetProperty("hasPassword").GetBoolean());
			Assert.True(json.GetProperty("isGoogleLinked").GetBoolean());
			Assert.Equal(created.User.Email, json.GetProperty("email").GetString());
		}
		using var forbidden = await SendAsync(host.Client, "profile/change-email", new { currentPassword = Password, newEmail = "new@example.test" }, HttpStatusCode.Forbidden);
		Assert.Equal("google_account_email_change_forbidden", (await forbidden.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
		await SendAsync(host.Client, "logout", new { }, HttpStatusCode.NoContent);
		await SendAsync(host.Client, "refresh", new { refreshToken = googleSession.RefreshToken }, HttpStatusCode.Unauthorized);
	}

	[PostgresTheory]
	[InlineData("gmail.com", true, null, false)]
	[InlineData("gmail.com", false, null, true)]
	[InlineData("example.test", true, "example.test", false)]
	[InlineData("example.test", true, null, true)]
	[InlineData("example.test", false, "example.test", true)]
	public async Task EmailAuthorityDeterminesConfirmationAndSession(string domain, bool verified, string? hostedDomain, bool requiresConfirmation)
	{
		await using var host = await StartHostAsync(_google);
		var identity = Identity("google.user@" + domain, verified: verified, domain: hostedDomain);
		var challenge = await ChallengeAsync(host.Client, identity);
		using var response = await SendAsync(host.Client, "google/register", Registration(challenge.IdToken), HttpStatusCode.Created, challenge.Cookie);
		var result = (await response.Content.ReadFromJsonAsync<GoogleRegistrationResponse>())!;
		Assert.Equal(requiresConfirmation, result.RequiresEmailConfirmation);
		Assert.Equal(!requiresConfirmation, result.User.IsEmailConfirmed);
		Assert.Equal(requiresConfirmation, result.Authentication is null);
		if (!requiresConfirmation)
		{
			Assert.Empty(Email.Messages);
			return;
		}
		var confirmation = Email.LastToken;
		await SendAsync(host.Client, "forgot-password", new { email = identity.Email }, HttpStatusCode.Accepted);
		Assert.Single(Email.Messages);
		var login = await ChallengeAsync(host.Client, identity);
		await SendAsync(host.Client, "google/login", new { idToken = login.IdToken }, HttpStatusCode.Forbidden, login.Cookie);
		await SendAsync(host.Client, "confirm-email", new { token = confirmation }, HttpStatusCode.NoContent);
		await SendAsync(host.Client, "google/login", new { idToken = login.IdToken }, HttpStatusCode.OK, login.Cookie);
	}

	[PostgresFact]
	public async Task ExistingLocalEmailIsNeverAutomaticallyLinkedAndInactiveSubjectIsNeverReused()
	{
		await using var host = await StartHostAsync(_google);
		var conflict = await ChallengeAsync(host.Client, Identity("anna@example.test"));
		await SendAsync(host.Client, "google/register", Registration(conflict.IdToken), HttpStatusCode.Conflict, conflict.Cookie);
		await using (var db = OpenDatabase())
		{
			Assert.Null((await db.Users.SingleAsync(u => u.Id == AnnaId)).GoogleSubject);
			await db.Users.Where(u => u.Id == AnnaId).ExecuteUpdateAsync(s => s.SetProperty(u => u.GoogleSubject, "google-123").SetProperty(u => u.IsActive, false));
		}
		var inactive = await ChallengeAsync(host.Client, Identity());
		await SendAsync(host.Client, "google/login", new { idToken = inactive.IdToken }, HttpStatusCode.Forbidden, inactive.Cookie);
		await SendAsync(host.Client, "google/register", Registration(inactive.IdToken), HttpStatusCode.Forbidden, inactive.Cookie);
	}

	[PostgresFact]
	public async Task MissingOrMismatchedBrowserCookieCannotUseAnotherChallenge()
	{
		await using var host = await StartHostAsync(_google);
		var first = await ChallengeAsync(host.Client, Identity());
		var other = await ChallengeAsync(host.Client, Identity());
		await SendAsync(host.Client, "google/register", Registration(first.IdToken), HttpStatusCode.Unauthorized);
		await SendAsync(host.Client, "google/register", Registration(first.IdToken), HttpStatusCode.Unauthorized, other.Cookie);
		await SendAsync(host.Client, "google/register", Registration(first.IdToken), HttpStatusCode.Created, first.Cookie);
	}

	[PostgresFact]
	public async Task ParallelRegistrationConsumesChallengeOnceAndCreatesOneAccountAndSession()
	{
		await using var host = await StartHostAsync(_google);
		var challenge = await ChallengeAsync(host.Client, Identity());
		var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/google/register") { Content = JsonContent.Create(Registration(challenge.IdToken)) };
			request.Headers.Add("Cookie", challenge.Cookie);
			using var response = await host.Client.SendAsync(request);
			return response.StatusCode;
		}));
		Assert.Single(results, status => status == HttpStatusCode.Created);
		Assert.All(results.Where(status => status != HttpStatusCode.Created), status => Assert.Contains(status, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Conflict }));
		await using var db = OpenDatabase();
		var user = Assert.Single(await db.Users.Where(u => u.GoogleSubject == "google-123").ToListAsync());
		Assert.Single(await db.UserTokens.Where(t => t.FkUserId == user.Id).ToListAsync());
	}

	[PostgresFact]
	public async Task EmailDeliveryFailureKeepsPendingAccountAndAllowsResend()
	{
		await using var host = await StartHostAsync(_google);
		Email.Fail = true;
		var challenge = await ChallengeAsync(host.Client, Identity("google@example.test"));
		await SendAsync(host.Client, "google/register", Registration(challenge.IdToken), HttpStatusCode.ServiceUnavailable, challenge.Cookie);
		await using (var db = OpenDatabase())
		{
			var user = await db.Users.SingleAsync(u => u.GoogleSubject == "google-123");
			Assert.False(user.IsEmailConfirmed);
			Assert.Null(user.PasswordHash);
			await db.UserTokens.Where(t => t.FkUserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddMinutes(-2)));
		}
		Email.Fail = false;
		await SendAsync(host.Client, "resend-confirmation", new { email = "google@example.test" }, HttpStatusCode.Accepted);
		await SendAsync(host.Client, "confirm-email", new { token = Email.LastToken }, HttpStatusCode.NoContent);
	}

	[PostgresFact]
	public async Task SessionInsertFailureRollsBackUserAndStarterGroup()
	{
		await using var host = await StartHostAsync(_google);
		await using (var connection = new NpgsqlConnection(ConnectionString))
		{
			await connection.OpenAsync();
			await using var command = new NpgsqlCommand("""
				CREATE FUNCTION public.reject_test_session() RETURNS trigger LANGUAGE plpgsql AS $$
				BEGIN IF NEW.purpose = 4 THEN RAISE EXCEPTION 'synthetic session failure'; END IF; RETURN NEW; END; $$;
				CREATE TRIGGER reject_test_session BEFORE INSERT ON "user".user_token FOR EACH ROW EXECUTE FUNCTION public.reject_test_session();
				""", connection);
			await command.ExecuteNonQueryAsync();
		}
		var challenge = await ChallengeAsync(host.Client, Identity());
		var failure = await Assert.ThrowsAsync<DbUpdateException>(() => SendAsync(host.Client, "google/register", Registration(challenge.IdToken), HttpStatusCode.Created, challenge.Cookie));
		Assert.Equal("synthetic session failure", Assert.IsType<PostgresException>(failure.InnerException).MessageText);
		await using var db = OpenDatabase();
		Assert.Equal(5, await db.Users.CountAsync());
		await using var check = new NpgsqlConnection(ConnectionString);
		await check.OpenAsync();
		await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM grouping.\"group\"", check);
		Assert.Equal(7L, await count.ExecuteScalarAsync());
	}

	[PostgresFact]
	public async Task GoogleMigrationRoundTripPreservesLocalUsersAndRefusesRemovingPasswordlessIdentity()
	{
		await using var app = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		var migrator = app.GetService<IMigrator>();
		var migrations = app.Database.GetMigrations().ToArray();
		var previousMigration = migrations[Array.FindIndex(migrations, m => m.EndsWith("_UserImprovements", StringComparison.Ordinal)) - 1];
		await app.Database.ExecuteSqlRawAsync("TRUNCATE limits.user_limit_alert_state, limits.group_limit_alert_state, limits.user_limit, limits.group_limit;");
		await migrator.MigrateAsync(previousMigration);
		await migrator.MigrateAsync();
		Assert.False(app.Database.HasPendingModelChanges());
		var userModel = app.Model.FindEntityType(typeof(Models.User))!;
		Assert.Equal("text", userModel.FindProperty(nameof(Models.User.GoogleSubject))!.GetColumnType());
		Assert.True(userModel.FindProperty(nameof(Models.User.PasswordHash))!.IsNullable);
		await using (var db = OpenDatabase())
		{
			db.Users.Add(new Models.User
			{
				Id = Guid.NewGuid(),
				Login = "google.user",
				Name = "Anna",
				Surname = "Testowa",
				Email = "google@gmail.com",
				GoogleSubject = "google-123",
				BirthDate = new DateOnly(2000, 1, 1),
				IsActive = true,
				IsEmailConfirmed = true
			});
			await db.SaveChangesAsync();
		}
		await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previousMigration));
		// A failed downgrade must leave the combined User migration intact.
		await migrator.MigrateAsync();
		await using var check = OpenDatabase();
		Assert.Equal("google-123", (await check.Users.SingleAsync(u => u.Login == "google.user")).GoogleSubject);
		Assert.Equal(5, await check.Users.CountAsync(u => u.PasswordHash != null));
	}

	[PostgresFact]
	public async Task IndependentChallengesCannotCreateDuplicateGoogleIdentityAndParallelLoginsKeepOneSession()
	{
		await using var host = await StartHostAsync(_google);
		var first = await ChallengeAsync(host.Client, Identity());
		var second = await ChallengeAsync(host.Client, Identity());
		var registrations = await Task.WhenAll(new[] { first, second }.Select(async challenge =>
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/google/register") { Content = JsonContent.Create(Registration(challenge.IdToken)) };
			request.Headers.Add("Cookie", challenge.Cookie);
			using var response = await host.Client.SendAsync(request);
			return response.StatusCode;
		}));
		Assert.Single(registrations, status => status == HttpStatusCode.Created);
		Assert.Single(registrations, status => status == HttpStatusCode.Conflict);
		var login1 = await ChallengeAsync(host.Client, Identity());
		var login2 = await ChallengeAsync(host.Client, Identity());
		await Task.WhenAll(new[] { login1, login2 }.Select(async challenge =>
		{
			using var response = await SendAsync(host.Client, "google/login", new { idToken = challenge.IdToken }, HttpStatusCode.OK, challenge.Cookie);
		}));
		await using var db = OpenDatabase();
		var user = await db.Users.SingleAsync(u => u.GoogleSubject == "google-123");
		Assert.Null(user.PasswordHash);
		Assert.Single(await db.UserTokens.Where(t => t.FkUserId == user.Id && t.Purpose == UserTokenPurpose.RefreshToken).ToListAsync());
	}

	[PostgresFact]
	public async Task GoogleEmailChangeIsBlockedBeforePasswordSetupAndForPreviouslyIssuedTokens()
	{
		await using var host = await StartHostAsync(_google);
		var challenge = await ChallengeAsync(host.Client, Identity());
		using var response = await SendAsync(host.Client, "google/register", Registration(challenge.IdToken), HttpStatusCode.Created, challenge.Cookie);
		var account = (await response.Content.ReadFromJsonAsync<GoogleRegistrationResponse>())!;
		host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.Authentication!.AccessToken);
		await SendAsync(host.Client, "profile/change-email", new { currentPassword = Password, newEmail = "new@example.test" }, HttpStatusCode.Forbidden);
		const string rawToken = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		await using (var db = OpenDatabase())
		{
			db.UserTokens.Add(new UserToken
			{
				Id = Guid.NewGuid(),
				FkUserId = account.User.Id,
				Purpose = UserTokenPurpose.EmailChange,
				TokenHash = new Services.Tokens.UserTokenGenerator().HashToken(rawToken),
				TargetEmail = "new@example.test",
				CreatedAt = DateTime.UtcNow,
				ExpiresAt = DateTime.UtcNow.AddHours(1)
			});
			await db.SaveChangesAsync();
		}
		await SendAsync(host.Client, "confirm-email-change", new { token = rawToken }, HttpStatusCode.BadRequest);
		await using var check = OpenDatabase();
		Assert.Equal(account.User.Email, (await check.Users.SingleAsync(u => u.Id == account.User.Id)).Email);
	}

	[PostgresFact]
	public async Task GoogleSubjectIsUniqueAcrossInactiveUsersAndAtLeastOneAuthenticationMethodIsRequired()
	{
		await using (var db = OpenDatabase())
		{
			await db.Users.Where(u => u.Id == AnnaId).ExecuteUpdateAsync(s => s.SetProperty(u => u.GoogleSubject, "google-123").SetProperty(u => u.IsActive, false));
			db.Users.Add(new Models.User
			{
				Id = Guid.NewGuid(),
				Login = "other.google",
				Name = "Anna",
				Surname = "Testowa",
				Email = "other@gmail.com",
				GoogleSubject = "google-123",
				BirthDate = new DateOnly(2000, 1, 1),
				IsActive = true
			});
			var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
			Assert.Equal("c_user_unique_google_subject", Assert.IsType<PostgresException>(duplicate.InnerException).ConstraintName);
		}
		await using (var db = OpenDatabase())
		{
			db.Users.Add(new Models.User
			{
				Id = Guid.NewGuid(),
				Login = "invalid.user",
				Name = "Anna",
				Surname = "Testowa",
				Email = "invalid@gmail.com",
				BirthDate = new DateOnly(2000, 1, 1),
				IsActive = true
			});
			var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
			Assert.Equal("c_user_authentication_method", Assert.IsType<PostgresException>(invalid.InnerException).ConstraintName);
		}
	}

	private async Task<(string Cookie, string IdToken)> ChallengeAsync(HttpClient client, GoogleIdentity identity)
	{
		using var response = await SendAsync(client, "google/challenge", new { }, HttpStatusCode.OK);
		var challenge = (await response.Content.ReadFromJsonAsync<GoogleChallengeResponse>())!;
		var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];
		var idToken = Guid.NewGuid().ToString("N");
		_google.Tokens[idToken] = (challenge.Nonce, identity);
		return (cookie, idToken);
	}

	private static object Registration(string idToken) => new { idToken, login = "google.user", name = "Anna", surname = "Testowa", birthDate = "2000-01-01" };

	private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string route, object body, HttpStatusCode expected, string? cookie = null)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/" + route) { Content = JsonContent.Create(body) };
		if (cookie is not null)
			request.Headers.Add("Cookie", cookie);
		var response = await client.SendAsync(request);
		Assert.Equal(expected, response.StatusCode);
		return response;
	}

	private sealed class FakeGoogleValidator : IGoogleIdTokenValidator
	{
		public ConcurrentDictionary<string, (string Nonce, GoogleIdentity Identity)> Tokens { get; } = new();
		public Task<GoogleIdentity?> ValidateAsync(string idToken, string expectedNonce, CancellationToken cancellationToken = default) =>
			Task.FromResult(Tokens.TryGetValue(idToken, out var token) && token.Nonce == expectedNonce ? token.Identity : null);
	}
}
