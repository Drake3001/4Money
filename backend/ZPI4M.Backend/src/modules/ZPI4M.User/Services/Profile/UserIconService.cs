using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZPI4M.Shared.Images;
using ZPI4M.Shared.Storage;

namespace ZPI4M.User.Services.Profile;

public sealed class UserIconService(
	IObjectStorage storage,
	IConfiguration config,
	ILogger<UserIconService> logger) : IUserIconService
{
	private string PresetPrefix => config["Storage:UserIconPresetPrefix"] ?? "icons/presets/users/";

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
		byte[] bytes;
		try
		{
			bytes = await IconImageProcessor.NormalizeAsync(content, contentType, cancellationToken);
		}
		catch (IconImageException exception)
		{
			throw new UserIconException(exception.Status);
		}
		using var normalized = new MemoryStream(bytes);
		var key = $"users/{userId}/icons/{Guid.NewGuid():N}";
		try
		{
			await storage.UploadAsync(key, normalized, IconImageProcessor.ContentType, cancellationToken);
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
