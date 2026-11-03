using ZPI4M.Shared.Storage;

namespace ZPI4M.Shared.Storage;

public interface IObjectStorage
{
	Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken cancellationToken = default);

	Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

	Task<FileUrlResult> GetReadUrlAsync(string key, CancellationToken cancellationToken = default);

	Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
