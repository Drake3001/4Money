namespace ZPI4M.Shared.Storage;

public class FileUrlResult
{
	public string Url { get; }
	public DateTimeOffset ExpiresAt { get; }

	public FileUrlResult(string url, DateTimeOffset expiresAt)
	{
		Url = url;
		ExpiresAt = expiresAt;
	}
}
