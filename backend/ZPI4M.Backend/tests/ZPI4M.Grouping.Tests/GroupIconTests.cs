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
	public async Task Invalid_signature_never_uploads(string mime)
	{
		var storage = new Storage();
		using var stream = new MemoryStream([1, 2, 3]);
		var error = await Assert.ThrowsAsync<GroupIconException>(() => Service(storage).UploadAsync(Guid.NewGuid(), stream, mime, default));
		Assert.Equal(415, error.Status);
		Assert.Null(storage.Key);
	}
	[Fact]
	public async Task Upload_generates_group_key_and_preserves_source()
	{
		var storage = new Storage();
		var id = Guid.NewGuid();
		using var stream = new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10]);
		var key = await Service(storage).UploadAsync(id, stream, "image/png", default);
		Assert.StartsWith($"groups/{id}/icons/", key);
		Assert.Equal(key, storage.Key);
		Assert.True(stream.CanRead);
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
		public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken cancellationToken = default) { Prefix = prefix; return Task.FromResult<IReadOnlyList<string>>(["icons/presets/groups/home.png", "icons/presets/groups/readme.txt"]); }
		public Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) { Key = key; return Task.CompletedTask; }
		public Task<FileUrlResult> GetReadUrlAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(new FileUrlResult("https://example.test", DateTimeOffset.UtcNow));
		public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
