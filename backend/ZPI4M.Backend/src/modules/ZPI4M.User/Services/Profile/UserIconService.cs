using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZPI4M.Shared.Storage;

namespace ZPI4M.User.Services.Profile;

public sealed class UserIconService(
	IObjectStorage storage,
	IConfiguration config,
	ILogger<UserIconService> logger) : IUserIconService
{
	private const int MaxFileBytes = 5 * 1024 * 1024;
	private string PresetPrefix => config["USER_ICON_PRESET_PREFIX"] ?? "icons/presets/users/";

	public async Task<IReadOnlyList<UserIconPreset>> GetPresetsAsync(CancellationToken cancellationToken = default)
	{
		var result = new List<UserIconPreset>();
		var keys = await storage.ListKeysAsync(PresetPrefix, cancellationToken);
		foreach (var key in keys.Where(IsImage).OrderBy(key => key, StringComparer.Ordinal))
		{
			var url = await storage.GetReadUrlAsync(key, cancellationToken);
			result.Add(new UserIconPreset(key, url.Url, url.ExpiresAt));
		}
		return result;
	}

	public async Task<bool> IsPresetAsync(string key, CancellationToken cancellationToken = default)
	{
		return IsImage(key) && (await storage.ListKeysAsync(PresetPrefix, cancellationToken)).Contains(key, StringComparer.Ordinal);
	}

	public async Task<string> UploadAsync(Guid userId, Stream content, string contentType, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(content);
		await using var buffer = new FileStream(
			Path.Combine(Path.GetTempPath(), $"user-icon-{Guid.NewGuid():N}"),
			FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
			FileOptions.Asynchronous | FileOptions.DeleteOnClose);
		var bytes = new byte[81920];
		int read;
		while ((read = await content.ReadAsync(bytes, cancellationToken)) > 0)
		{
			if (buffer.Length + read > MaxFileBytes)
				throw new UserIconException(413);
			await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken);
		}
		if (buffer.Length == 0)
			throw new UserIconException(400);

		buffer.Position = 0;
		var header = new byte[12];
		var count = await buffer.ReadAsync(header, cancellationToken);
		var png = count >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
		var jpeg = count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255;
		var webp = count == 12 && Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WEBP";
		if (!(contentType == "image/png" && png || contentType == "image/jpeg" && jpeg || contentType == "image/webp" && webp))
			throw new UserIconException(415);

		buffer.Position = 0;
		var key = $"users/{userId}/icons/{Guid.NewGuid():N}";
		try
		{
			await storage.UploadAsync(key, buffer, contentType, cancellationToken);
		}
		catch
		{
			await CleanupAsync(key);
			throw;
		}
		return key;
	}

	public Task<FileUrlResult> GetUrlAsync(string key, CancellationToken cancellationToken = default) =>
		storage.GetReadUrlAsync(key, cancellationToken);

	public async Task CleanupAsync(string key)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		try
		{
			await storage.DeleteAsync(key, timeout.Token);
		}
		catch (Exception exception)
		{
			logger.LogWarning("User icon cleanup failed ({ErrorType}).", exception.GetType().Name);
		}
	}

	private static bool IsImage(string key) =>
		Path.GetExtension(key).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";
}
