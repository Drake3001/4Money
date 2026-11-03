using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace ZPI4M.Shared.Storage;

public sealed class S3ObjectStorage : IObjectStorage
{
	private readonly IAmazonS3 _client;
	private readonly S3StorageOptions _options;
	private readonly TimeProvider _timeProvider;

	public S3ObjectStorage(IAmazonS3 client, IOptions<S3StorageOptions> options, TimeProvider timeProvider)
	{
		_client = client;
		_options = options.Value;
		_timeProvider = timeProvider;
	}

	public async Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
		var keys = new List<string>();
		string? token = null;
		do
		{
			var page = await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _options.Bucket, Prefix = prefix, ContinuationToken = token }, cancellationToken);
			keys.AddRange((page.S3Objects ?? []).Select(o => o.Key).Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && !k.EndsWith('/')));
			token = page.IsTruncated == true ? page.NextContinuationToken : null;
		} while (token is not null);
		return keys;
	}

	public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentNullException.ThrowIfNull(content);
		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
		if (!content.CanRead)
			throw new ArgumentException("The content stream must be readable.", nameof(content));
		cancellationToken.ThrowIfCancellationRequested();

		if (content.CanSeek)
		{
			await UploadSeekableAsync(key, content, contentType, cancellationToken);
			return;
		}

		var path = Path.Combine(Path.GetTempPath(), $"zpi4m-storage-{Guid.NewGuid():N}.tmp");
		await using var buffer = new FileStream(path, new FileStreamOptions
		{
			Mode = FileMode.CreateNew,
			Access = FileAccess.ReadWrite,
			Share = FileShare.None,
			Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
		});
		await content.CopyToAsync(buffer, cancellationToken);
		buffer.Position = 0;
		await UploadSeekableAsync(key, buffer, contentType, cancellationToken);
	}

	private async Task UploadSeekableAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
	{
		if (content.Length <= content.Position)
			throw new ArgumentException("The content stream must not be empty.", nameof(content));

		await _client.PutObjectAsync(new PutObjectRequest
		{
			BucketName = _options.Bucket,
			Key = key,
			InputStream = content,
			ContentType = contentType,
			AutoCloseStream = false,
			AutoResetStreamPosition = false,
			UseChunkEncoding = false
		}, cancellationToken);
	}

	public async Task<FileUrlResult> GetReadUrlAsync(string key, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		cancellationToken.ThrowIfCancellationRequested();
		var now = _timeProvider.GetUtcNow();
		var expiresAt = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds()).AddMinutes(_options.UrlExpirationMinutes);
		// Signing is local: the browser endpoint need not be reachable from this container.
		using var publicClient = _options.PublicEndpoint is null ? null : _options.CreateClient(usePublicEndpoint: true);
		var url = await (publicClient ?? _client).GetPreSignedURLAsync(new GetPreSignedUrlRequest
		{
			BucketName = _options.Bucket,
			Key = key,
			Verb = HttpVerb.GET,
			Protocol = new Uri(_options.PublicEndpoint ?? _options.Endpoint).Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS,
			Expires = expiresAt.UtcDateTime
		});
		cancellationToken.ThrowIfCancellationRequested();
		return new FileUrlResult(url, expiresAt);
	}

	public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			await _client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _options.Bucket, Key = key }, cancellationToken);
		}
		catch (AmazonS3Exception error) when (error.ErrorCode == "NoSuchKey")
		{
			// Deleting an already absent object is successful; other errors propagate.
		}
	}
}
