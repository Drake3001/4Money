using System.Collections.Concurrent;
using ZPI4M.Shared.Storage;

namespace ZPI4M.User.IntegrationTests;

public sealed class RecordingObjectStorage : IObjectStorage
{
	public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
	public ConcurrentQueue<string> Deleted { get; } = new();
	public bool FailUpload { get; set; }
	public bool FailDelete { get; set; }
	public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<string>>(Objects.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray());
	public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
	{
		using var buffer = new MemoryStream();
		await content.CopyToAsync(buffer, cancellationToken);
		Objects[key] = buffer.ToArray();
		if (FailUpload) throw new HttpRequestException("synthetic storage failure");
	}
	public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		Deleted.Enqueue(key);
		if (FailDelete) throw new HttpRequestException("synthetic delete failure");
		Objects.TryRemove(key, out _);
		return Task.CompletedTask;
	}
	public Task<FileUrlResult> GetReadUrlAsync(string key, CancellationToken cancellationToken = default) =>
		Task.FromResult(new FileUrlResult("https://storage.test/" + key, DateTimeOffset.UtcNow.AddMinutes(15)));
}
