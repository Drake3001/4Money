using ZPI4M.Receipt.DTOs;

namespace ZPI4M.Receipt.Services;

public interface IFileStorageService
{
	Task<string> UploadAsync(Stream content, string contentType, CancellationToken cancellationToken = default);

	Task<FileUrlResult> GetUrlAsync(string key, CancellationToken cancellationToken = default);

	Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
