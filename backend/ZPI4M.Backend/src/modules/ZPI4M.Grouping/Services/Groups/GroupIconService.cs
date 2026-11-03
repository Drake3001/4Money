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
		await using var buffer = new FileStream(Path.Combine(Path.GetTempPath(), $"group-icon-{Guid.NewGuid():N}"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
		var bytes = new byte[81920];
		int read;
		while ((read = await content.ReadAsync(bytes, ct)) > 0)
		{
			if (buffer.Length + read > 5 * 1024 * 1024) throw new GroupIconException(413);
			await buffer.WriteAsync(bytes.AsMemory(0, read), ct);
		}
		if (buffer.Length == 0) throw new GroupIconException(400);
		buffer.Position = 0;
		var header = new byte[12];
		var count = await buffer.ReadAsync(header, ct);
		var png = count >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
		var jpeg = count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255;
		var webp = count == 12 && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP";
		if (!(contentType == "image/png" && png || contentType == "image/jpeg" && jpeg || contentType == "image/webp" && webp)) throw new GroupIconException(415);
		buffer.Position = 0;
		var key = $"groups/{groupId}/icons/{Guid.NewGuid():N}";
		await storage.UploadAsync(key, buffer, contentType, ct);
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
