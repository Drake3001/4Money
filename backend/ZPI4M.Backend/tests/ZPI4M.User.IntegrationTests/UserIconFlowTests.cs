using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using ZPI4M.Database;
using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.DTOs.Profile;

namespace ZPI4M.User.IntegrationTests;

public sealed class UserIconFlowTests : PostgresFixture
{
	private const string Preset = "icons/presets/users/anna.png";
	private static readonly byte[] Png = CreatePng();

	private static byte[] CreatePng()
	{
		using var image = new MagickImage(MagickColors.Red, 32, 32);
		return image.ToByteArray(MagickFormat.Png);
	}

	[PostgresFact]
	public async Task UploadPreservePresetRemoveAndSignedReadUseOnlyAuthenticatedOwner()
	{
		Storage.Objects[Preset] = Png;
		await using var host = await StartHostAsync();
		await AuthenticateAsync(host.Client);
		using var uploaded = await UploadAsync(host.Client);
		Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
		var profile = (await uploaded.Content.ReadFromJsonAsync<UserProfileResponse>())!;
		var key = Assert.IsType<string>(profile.IconStorageKey);
		Assert.StartsWith($"users/{AnnaId}/icons/", key);
		Assert.Null(profile.Icon);
		using (var saved = new MagickImage(Storage.Objects[key]))
		{
			Assert.Equal(MagickFormat.WebP, saved.Format);
			Assert.Equal(32u, saved.Width);
			Assert.Equal(32u, saved.Height);
		}
		using (var url = await host.Client.GetAsync("/api/users/profile/icon"))
		{
			Assert.Equal(HttpStatusCode.OK, url.StatusCode);
			Assert.Contains(key, await url.Content.ReadAsStringAsync());
		}
		using (var preserved = await host.Client.PutAsJsonAsync("/api/users/profile", Profile()))
		{
			Assert.Equal(HttpStatusCode.OK, preserved.StatusCode);
			Assert.Equal(key, (await preserved.Content.ReadFromJsonAsync<UserProfileResponse>())!.IconStorageKey);
		}
		Assert.Empty(Storage.Deleted);
		using (var preset = await host.Client.PutAsJsonAsync("/api/users/profile", Profile(Preset)))
		{
			Assert.Equal(HttpStatusCode.OK, preset.StatusCode);
			var chosen = (await preset.Content.ReadFromJsonAsync<UserProfileResponse>())!;
			Assert.Equal(Preset, chosen.Icon);
			Assert.Null(chosen.IconStorageKey);
		}
		Assert.False(Storage.Objects.ContainsKey(key));
		using (var presets = await host.Client.GetAsync("/api/users/profile/icon-presets"))
			Assert.Contains(Preset, await presets.Content.ReadAsStringAsync());
		using (var removed = await host.Client.PutAsJsonAsync("/api/users/profile", Profile(remove: true)))
			Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
		Assert.True(Storage.Objects.ContainsKey(Preset));
		using (var missing = await host.Client.GetAsync("/api/users/profile/icon"))
			Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
		await using var db = OpenDatabase();
		Assert.Null((await db.Users.SingleAsync(u => u.Login == "seed_bartek")).IconStorageKey);
	}

	[PostgresTheory]
	[InlineData("image/jpeg", false, false, 415)]
	[InlineData("image/png", true, false, 400)]
	[InlineData("image/png", false, true, 413)]
	public async Task InvalidUploadsDoNotModifyProfileOrStorage(string contentType, bool selectPreset, bool oversized, int status)
	{
		Storage.Objects[Preset] = Png;
		await using var host = await StartHostAsync();
		await AuthenticateAsync(host.Client);
		using var response = await UploadAsync(host.Client, mime: contentType, preset: selectPreset ? Preset : null, oversized: oversized);
		Assert.Equal(status, (int)response.StatusCode);
		Assert.Single(Storage.Objects);
		await using var db = OpenDatabase();
		Assert.Null((await db.Users.SingleAsync(u => u.Id == AnnaId)).IconStorageKey);
	}

	[PostgresFact]
	public async Task UnknownPresetAndClientProvidedStorageKeyCannotSelectForeignObject()
	{
		await using var host = await StartHostAsync();
		await AuthenticateAsync(host.Client);
		using var invalid = await host.Client.PutAsJsonAsync("/api/users/profile", Profile("users/another-user/icons/private.png"));
		Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
		Assert.Empty(Storage.Deleted);
	}

	[PostgresFact]
	public async Task FailedDatabaseUpdateRollsBackProfileAndDeletesOnlyNewUpload()
	{
		await using var host = await StartHostAsync();
		await AuthenticateAsync(host.Client);
		var oldKey = await SeedOwnImageAsync();
		await using (var connection = new NpgsqlConnection(ConnectionString))
		{
			await connection.OpenAsync();
			await using var command = new NpgsqlCommand("""
				CREATE FUNCTION public.reject_test_profile() RETURNS trigger LANGUAGE plpgsql AS $$
				BEGIN RAISE EXCEPTION 'synthetic profile failure'; END; $$;
				CREATE TRIGGER reject_test_profile BEFORE UPDATE ON "user"."user" FOR EACH ROW EXECUTE FUNCTION public.reject_test_profile();
				""", connection);
			await command.ExecuteNonQueryAsync();
		}
		var error = await Assert.ThrowsAsync<PostgresException>(() => UploadAsync(host.Client));
		Assert.Equal("synthetic profile failure", error.MessageText);
		Assert.Equal(oldKey, Assert.Single(Storage.Objects).Key);
		Assert.NotEqual(oldKey, Assert.Single(Storage.Deleted));
		await using var db = OpenDatabase();
		Assert.Equal(oldKey, (await db.Users.SingleAsync(u => u.Id == AnnaId)).IconStorageKey);
	}

	[PostgresFact]
	public async Task StorageUploadFailureDoesNotChangeOldImageAndDeletionFailureDoesNotUndoCommittedProfile()
	{
		await using var host = await StartHostAsync();
		await AuthenticateAsync(host.Client);
		var oldKey = await SeedOwnImageAsync();
		Storage.FailUpload = true;
		using (var failed = await UploadAsync(host.Client))
			Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
		Assert.Equal(oldKey, Assert.Single(Storage.Objects).Key);
		Storage.FailUpload = false;
		Storage.FailDelete = true;
		using var updated = await UploadAsync(host.Client);
		Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
		var profile = (await updated.Content.ReadFromJsonAsync<UserProfileResponse>())!;
		Assert.NotEqual(oldKey, profile.IconStorageKey);
		Assert.True(Storage.Objects.ContainsKey(oldKey));
		await using var db = OpenDatabase();
		Assert.Equal(profile.IconStorageKey, (await db.Users.SingleAsync(u => u.Id == AnnaId)).IconStorageKey);
	}

	[PostgresFact]
	public async Task ConcurrentReplacementsLeaveOneSelectedFileAndNeverDeleteIt()
	{
		await using var host = await StartHostAsync();
		await AuthenticateAsync(host.Client);
		await SeedOwnImageAsync();
		await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
		{
			using var response = await UploadAsync(host.Client);
			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		}));
		await using var db = OpenDatabase();
		var key = (await db.Users.SingleAsync(u => u.Id == AnnaId)).IconStorageKey;
		Assert.Equal(key, Assert.Single(Storage.Objects).Key);
		Assert.DoesNotContain(key!, Storage.Deleted);
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task UncertainCommitKeepsBothFilesWhetherCommitReachedDatabase(bool afterCommit)
	{
		var interceptor = new CommitFailure(afterCommit);
		await using var host = await StartHostAsync(interceptor: interceptor);
		await AuthenticateAsync(host.Client);
		var oldKey = await SeedOwnImageAsync();
		interceptor.Enabled = true;
		await Assert.ThrowsAsync<IOException>(() => UploadAsync(host.Client));
		Assert.Equal(2, Storage.Objects.Count);
		Assert.Empty(Storage.Deleted);
		await using var db = OpenDatabase();
		var key = (await db.Users.SingleAsync(u => u.Id == AnnaId)).IconStorageKey;
		Assert.Equal(!afterCommit, key == oldKey);
		Assert.True(Storage.Objects.ContainsKey(key!));
	}

	[PostgresFact]
	public async Task IconMigrationRoundTripAndExclusiveSourceConstraint()
	{
		await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
		var migrator = db.GetService<IMigrator>();
		var migrations = db.Database.GetMigrations().ToArray();
		var previousMigration = migrations[Array.FindIndex(migrations, m => m.EndsWith("_UserImprovements", StringComparison.Ordinal)) - 1];
		var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
			"UPDATE \"user\".\"user\" SET icon = 'preset.png', icon_storage_key = 'users/synthetic/icons/file' WHERE login = 'seed_anna'"));
		Assert.Equal("c_user_icon_source", failure.ConstraintName);
		await db.Database.ExecuteSqlRawAsync("TRUNCATE limits.user_limit_alert_state, limits.group_limit_alert_state, limits.user_limit, limits.group_limit;");
		await migrator.MigrateAsync(previousMigration);
		await migrator.MigrateAsync();
		Assert.False(db.Database.HasPendingModelChanges());
		Assert.Equal(5, await db.Set<Models.User>().CountAsync());
	}

	private async Task<string> SeedOwnImageAsync()
	{
		var key = $"users/{AnnaId}/icons/original";
		Storage.Objects[key] = Png;
		await using var db = OpenDatabase();
		await db.Users.Where(u => u.Id == AnnaId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IconStorageKey, key).SetProperty(u => u.Icon, (string?)null));
		return key;
	}

	private static object Profile(string? preset = null, bool remove = false) => new
	{
		login = "seed_anna",
		name = "Anna",
		surname = "Testowa",
		birthDate = "2000-01-01",
		icon = preset,
		removeIcon = remove
	};

	private static async Task AuthenticateAsync(HttpClient client)
	{
		using var response = await client.PostAsJsonAsync("/api/users/login", new { login = "seed_anna", password = Password });
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!.AccessToken);
	}

	private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string mime = "image/png", string? preset = null, bool oversized = false)
	{
		using var form = new MultipartFormDataContent();
		foreach (var (key, value) in new[] { ("login", "seed_anna"), ("name", "Anna"), ("surname", "Testowa"), ("birthDate", "2000-01-01") })
			form.Add(new StringContent(value), key);
		if (preset is not null) form.Add(new StringContent(preset), "icon");
		var bytes = oversized ? new byte[5 * 1024 * 1024 + 1] : Png;
		if (oversized) Png.CopyTo(bytes, 0);
		using var image = new ByteArrayContent(bytes);
		image.Headers.ContentType = new(mime);
		form.Add(image, "iconFile", "avatar.png");
		return await client.PutAsync("/api/users/profile?userId=" + Guid.NewGuid(), form);
	}

	private sealed class CommitFailure(bool afterCommit) : DbTransactionInterceptor
	{
		public bool Enabled { get; set; }
		public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
			InterceptionResult result, CancellationToken cancellationToken = default)
		{
			if (Enabled && !afterCommit) throw new IOException("synthetic commit failure");
			return ValueTask.FromResult(result);
		}
		public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
		{
			if (Enabled && afterCommit) throw new IOException("synthetic commit acknowledgement failure");
			return Task.CompletedTask;
		}
	}
}
