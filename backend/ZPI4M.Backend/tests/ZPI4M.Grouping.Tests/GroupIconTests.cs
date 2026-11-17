using ImageMagick;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZPI4M.Grouping.Services.Groups;
using ZPI4M.Shared.Storage;
public sealed class GroupIconTests
{
	[Fact]
	public async Task Description_over_limit_is_rejected_before_any_io()
	{
		var service = new GroupService(null!, null!, null!, new ValidationOnlyTransactions(), null!, null!);
		var result = await service.CreateAsync(Guid.NewGuid(), new ZPI4M.Grouping.DTOs.Groups.CreateGroupRequest { Name = "Test", CurrencyCode = "PLN", Description = string.Concat(Enumerable.Repeat("😀", 256)) });
		Assert.Equal(400, result.Status);
	}

	[Fact]
	public async Task Catalog_uses_storage_and_filters_non_images()
	{
		var storage = new Storage();
		var service = Service(storage);
		var result = await service.GetPresetsAsync(default);
		Assert.Single(result);
		Assert.Equal("icons/presets/groups/home.png", result[0].Key);
		Assert.Equal("icons/presets/groups/", storage.Prefix);
	}
	[Theory]
	[InlineData("image/png")]
	[InlineData("image/jpeg")]
	[InlineData("image/webp")]
	[InlineData("image/heic")]
	[InlineData("image/heif")]
	public async Task Invalid_signature_never_uploads(string mime)
	{
		var storage = new Storage();
		using var stream = new MemoryStream([1, 2, 3]);
		var error = await Assert.ThrowsAsync<GroupIconException>(() => Service(storage).UploadAsync(Guid.NewGuid(), stream, mime, default));
		Assert.Equal(415, error.Status);
		Assert.Null(storage.Key);
	}
	[Theory]
	[InlineData("image/png", MagickFormat.Png)]
	[InlineData("image/jpeg", MagickFormat.Jpeg)]
	[InlineData("image/webp", MagickFormat.WebP)]
	[InlineData("image/heic", MagickFormat.Heic)]
	[InlineData("image/heif", MagickFormat.Heic)]
	public async Task Upload_generates_group_key_and_normalizes_image(string mime, MagickFormat format)
	{
		var storage = new Storage();
		var id = Guid.NewGuid();
		using var image = new MagickImage(MagickColors.Red, 1280, 640);
		var bytes = format == MagickFormat.Heic
			? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "profile-synthetic.heic"))
			: image.ToByteArray(format);
		using var stream = new MemoryStream(bytes);
		var key = await Service(storage).UploadAsync(id, stream, mime, default);
		Assert.StartsWith($"groups/{id}/icons/", key);
		Assert.Equal(key, storage.Key);
		Assert.True(stream.CanRead);
		Assert.Equal("image/webp", storage.ContentType);
		using var saved = new MagickImage(storage.Bytes!);
		Assert.Equal(MagickFormat.WebP, saved.Format);
		Assert.Equal(512u, saved.Width);
		Assert.Equal(256u, saved.Height);
		Assert.Empty(saved.ProfileNames);
	}

	[Fact]
	public async Task Truncated_image_never_uploads()
	{
		var storage = new Storage();
		using var stream = new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10]);
		Assert.Equal(415, (await Assert.ThrowsAsync<GroupIconException>(() => Service(storage).UploadAsync(Guid.NewGuid(), stream, "image/png", default))).Status);
		Assert.Null(storage.Key);
	}

	[Fact]
	public async Task Failed_storage_upload_cleans_its_key()
	{
		var storage = new Storage { FailUpload = true };
		using var image = new MagickImage(MagickColors.Red, 16, 16);
		using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
		await Assert.ThrowsAsync<HttpRequestException>(() => Service(storage).UploadAsync(Guid.NewGuid(), stream, "image/png", default));
		Assert.Equal(storage.Key, Assert.Single(storage.Deleted));
	}
	[Fact]
	public async Task Oversized_image_never_uploads()
	{
		var storage = new Storage();
		using var stream = new MemoryStream(new byte[5 * 1024 * 1024 + 1]);
		Assert.Equal(413, (await Assert.ThrowsAsync<GroupIconException>(() => Service(storage).UploadAsync(Guid.NewGuid(), stream, "image/png", default))).Status);
		Assert.Null(storage.Key);
	}
	private static GroupIconService Service(Storage s) => new(s, new ConfigurationBuilder().Build(), NullLogger<GroupIconService>.Instance);
	private sealed class Storage : IObjectStorage
	{
		public string? Prefix;
		public string? Key;
		public byte[]? Bytes;
		public string? ContentType;
		public bool FailUpload;
		public List<string> Deleted = [];
		public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken cancellationToken = default) { Prefix = prefix; return Task.FromResult<IReadOnlyList<string>>(["icons/presets/groups/home.png", "icons/presets/groups/readme.txt"]); }
		public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
		{
			Key = key;
			ContentType = contentType;
			using var buffer = new MemoryStream();
			await content.CopyToAsync(buffer, cancellationToken);
			Bytes = buffer.ToArray();
			if (FailUpload) throw new HttpRequestException("Synthetic storage failure");
		}
		public Task<FileUrlResult> GetReadUrlAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(new FileUrlResult("https://example.test", DateTimeOffset.UtcNow));
		public Task DeleteAsync(string key, CancellationToken cancellationToken = default) { Deleted.Add(key); return Task.CompletedTask; }
	}
}
