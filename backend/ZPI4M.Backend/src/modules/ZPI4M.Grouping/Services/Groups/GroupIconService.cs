using ZPI4M.Shared.Images;
using ZPI4M.Shared.Storage;
namespace ZPI4M.Grouping.Services.Groups;

public sealed class GroupIconService(IObjectStorage storage, Microsoft.Extensions.Configuration.IConfiguration config,
 Microsoft.Extensions.Logging.ILogger<GroupIconService> logger) : IGroupIconService
{
	private string Prefix => config["GROUP_ICON_PRESET_PREFIX"] ?? "icons/presets/groups/";
	private static bool Image(string key) => new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(key).ToLowerInvariant());
	public async Task<bool> IsPresetAsync(string key, CancellationToken ct) => (await storage.ListKeysAsync(Prefix, ct)).Any(k => k == key && Image(k));
	public async Task<IReadOnlyList<GroupIconPreset>> GetPresetsAsync(CancellationToken ct)
	{
		var result = new List<GroupIconPreset>();
		foreach (var key in (await storage.ListKeysAsync(Prefix, ct)).Where(Image).OrderBy(k => k, StringComparer.Ordinal))
		{
			var url = await storage.GetReadUrlAsync(key, ct);
			result.Add(new(key, url.Url, url.ExpiresAt));
		}
		return result;
	}
	public async Task<string> UploadAsync(Guid groupId, Stream content, string contentType, CancellationToken ct)
	{
		byte[] bytes;
		try
		{
			bytes = await IconImageProcessor.NormalizeAsync(content, contentType, ct);
		}
		catch (IconImageException exception)
		{
			throw new GroupIconException(exception.Status);
		}
		using var normalized = new MemoryStream(bytes);
		var key = $"groups/{groupId}/icons/{Guid.NewGuid():N}";
		try
		{
			await storage.UploadAsync(key, normalized, IconImageProcessor.ContentType, ct);
		}
		catch
		{
			await CleanupAsync(key);
			throw;
		}
		return key;
	}
	public Task<FileUrlResult> GetUrlAsync(string key, CancellationToken ct) => storage.GetReadUrlAsync(key, ct);
	public async Task CleanupAsync(string key)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		try { await storage.DeleteAsync(key, timeout.Token); }
		catch (Exception error) { Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, "Group icon cleanup failed ({ErrorType}).", error.GetType().Name); }
	}
}
