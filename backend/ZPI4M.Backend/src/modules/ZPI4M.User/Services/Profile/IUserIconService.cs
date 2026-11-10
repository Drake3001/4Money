using ZPI4M.Shared.Storage;

namespace ZPI4M.User.Services.Profile;

public interface IUserIconService
{
	Task<IReadOnlyList<UserIconPreset>> GetPresetsAsync(CancellationToken cancellationToken = default);
	Task<bool> IsPresetAsync(string key, CancellationToken cancellationToken = default);
	Task<string> UploadAsync(Guid userId, Stream content, string contentType, CancellationToken cancellationToken = default);
	Task<FileUrlResult> GetUrlAsync(string key, CancellationToken cancellationToken = default);
	Task CleanupAsync(string key);
}
