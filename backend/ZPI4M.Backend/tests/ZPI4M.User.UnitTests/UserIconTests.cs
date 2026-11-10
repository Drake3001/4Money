using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZPI4M.Shared.Storage;
using ZPI4M.User.Services.Profile;

namespace ZPI4M.User.UnitTests;

public sealed class UserIconTests
{
	[Theory]
	[InlineData("image/png", "89504E470D0A1A0A")]
	[InlineData("image/jpeg", "FFD8FF00")]
	[InlineData("image/webp", "524946460000000057454250")]
	public async Task UploadValidatesSignatureAndStoresBytesUnderUniqueUserKey(string mime, string hex)
	{
		var storage = new Storage();
		var service = Create(storage);
		var userId = Guid.NewGuid();
		var bytes = Convert.FromHexString(hex);
		using var content = new MemoryStream(bytes);
		var key = await service.UploadAsync(userId, content, mime);
		Assert.StartsWith($"users/{userId}/icons/", key);
		Assert.Equal(bytes, storage.Bytes);
		Assert.Equal(mime, storage.ContentType);
		content.Position = 0;
		Assert.NotEqual(key, await service.UploadAsync(userId, content, mime));
	}

	[Theory]
	[InlineData("image/jpeg", "89504E470D0A1A0A", 415)]
	[InlineData("image/svg+xml", "3C7376673E", 415)]
	[InlineData("image/png", "", 400)]
	[InlineData("image/webp", "52494646", 415)]
	public async Task InvalidContentNeverReachesStorage(string mime, string hex, int status)
	{
		var storage = new Storage();
		using var content = new MemoryStream(Convert.FromHexString(hex));
		var error = await Assert.ThrowsAsync<UserIconException>(() => Create(storage).UploadAsync(Guid.NewGuid(), content, mime));
		Assert.Equal(status, error.Status);
		Assert.Null(storage.Bytes);
	}

	[Theory]
	[InlineData(5 * 1024 * 1024, true)]
	[InlineData(5 * 1024 * 1024 + 1, false)]
	public async Task UploadEnforcesExactFiveMiBLimit(int length, bool accepted)
	{
		var bytes = new byte[length];
		Convert.FromHexString("89504E470D0A1A0A").CopyTo(bytes, 0);
		var storage = new Storage();
		using var content = new MemoryStream(bytes);
		if (accepted)
			await Create(storage).UploadAsync(Guid.NewGuid(), content, "image/png");
		else
			Assert.Equal(413, (await Assert.ThrowsAsync<UserIconException>(() => Create(storage).UploadAsync(Guid.NewGuid(), content, "image/png"))).Status);
		Assert.Equal(accepted, storage.Bytes is not null);
	}

	[Fact]
	public async Task PresetsUseConfiguredPrefixFilterImagesAndProduceExpiringUrls()
	{
		var storage = new Storage { Keys = ["custom/z.webp", "custom/a.PNG", "custom/readme.txt"] };
		var service = Create(storage, "custom/");
		var presets = await service.GetPresetsAsync();
		Assert.Equal("custom/", storage.Prefix);
		Assert.Equal(new[] { "custom/a.PNG", "custom/z.webp" }, presets.Select(p => p.Key));
		Assert.All(presets, preset => Assert.True(preset.ExpiresAt > DateTimeOffset.UtcNow));
		Assert.True(await service.IsPresetAsync("custom/z.webp"));
		Assert.False(await service.IsPresetAsync("custom/missing.webp"));
		Assert.False(await service.IsPresetAsync("custom/readme.txt"));
		Assert.Equal("https://storage.test/key", (await service.GetUrlAsync("key")).Url);
	}

	[Fact]
	public async Task FailedUploadCleansItsUnassignedKeyAndCleanupFailureDoesNotHideOriginalError()
	{
		var storage = new Storage { FailUpload = true, FailDelete = true };
		using var content = new MemoryStream(Convert.FromHexString("89504E470D0A1A0A"));
		var error = await Assert.ThrowsAsync<HttpRequestException>(() => Create(storage).UploadAsync(Guid.NewGuid(), content, "image/png"));
		Assert.Equal("upload failed", error.Message);
		Assert.Equal(storage.UploadKey, Assert.Single(storage.Deleted));
	}

	[Fact]
	public async Task CancelledUploadDoesNotWriteStorage()
	{
		var storage = new Storage();
		using var content = new MemoryStream([1]);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(storage).UploadAsync(Guid.NewGuid(), content, "image/png", cancellation.Token));
		Assert.Null(storage.Bytes);
	}

	private static UserIconService Create(Storage storage, string? prefix = null) => new(storage,
		new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["USER_ICON_PRESET_PREFIX"] = prefix }).Build(),
		NullLogger<UserIconService>.Instance);

	private sealed class Storage : IObjectStorage
	{
		public string[] Keys { get; init; } = [];
		public string? Prefix { get; private set; }
		public byte[]? Bytes { get; private set; }
		public string? ContentType { get; private set; }
		public string? UploadKey { get; private set; }
		public List<string> Deleted { get; } = [];
		public bool FailUpload { get; init; }
		public bool FailDelete { get; init; }
		public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken cancellationToken = default)
		{
			Prefix = prefix;
			return Task.FromResult<IReadOnlyList<string>>(Keys);
		}
		public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
		{
			UploadKey = key;
			ContentType = contentType;
			using var buffer = new MemoryStream();
			await content.CopyToAsync(buffer, cancellationToken);
			Bytes = buffer.ToArray();
			if (FailUpload) throw new HttpRequestException("upload failed");
		}
		public Task<FileUrlResult> GetReadUrlAsync(string key, CancellationToken cancellationToken = default) =>
			Task.FromResult(new FileUrlResult("https://storage.test/" + key, DateTimeOffset.UtcNow.AddMinutes(15)));
		public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
		{
			Deleted.Add(key);
			if (FailDelete) throw new HttpRequestException("delete failed");
			return Task.CompletedTask;
		}
	}
}
