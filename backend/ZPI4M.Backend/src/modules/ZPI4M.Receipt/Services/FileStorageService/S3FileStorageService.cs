using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using ZPI4M.Receipt.DTOs;

namespace ZPI4M.Receipt.Services;

public sealed class S3FileStorageService : IFileStorageService
{
	private readonly IAmazonS3 _client;
	private readonly S3StorageOptions _options;
	private readonly TimeProvider _timeProvider;

	public S3FileStorageService(IAmazonS3 client, IOptions<S3StorageOptions> options, TimeProvider timeProvider)
	{
		_client = client;
		_options = options.Value;
		_timeProvider = timeProvider;
	}

	public async Task<string> UploadAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
		if (!content.CanRead)
			throw new ArgumentException("The content stream must be readable.", nameof(content));
		cancellationToken.ThrowIfCancellationRequested();

		if (content.CanSeek)
			return await UploadSeekableAsync(content, contentType, cancellationToken);

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
		return await UploadSeekableAsync(buffer, contentType, cancellationToken);
	}

	private async Task<string> UploadSeekableAsync(Stream content, string contentType, CancellationToken cancellationToken)
	{
		if (content.Length <= content.Position)
			throw new ArgumentException("The content stream must not be empty.", nameof(content));

		var key = $"receipts/{Guid.NewGuid():N}";
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
		return key;
	}

	public async Task<FileUrlResult> GetUrlAsync(string key, CancellationToken cancellationToken = default)
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
