using ZPI4M.Shared.Storage;

namespace ZPI4M.Grouping.Services.Groups;

public interface IGroupIconService
{
	Task<IReadOnlyList<GroupIconPreset>> GetPresetsAsync(CancellationToken ct);
	Task<bool> IsPresetAsync(string key, CancellationToken ct);
	Task<string> UploadAsync(Guid groupId, Stream content, string contentType, CancellationToken ct);
	Task<FileUrlResult> GetUrlAsync(string key, CancellationToken ct);
	Task CleanupAsync(string key);
}
