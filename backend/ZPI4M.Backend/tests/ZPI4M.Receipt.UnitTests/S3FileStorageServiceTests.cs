using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Receipt.Services;

namespace ZPI4M.Receipt.UnitTests;

public sealed class S3FileStorageServiceTests
{
	[Fact]
	public async Task Real_SDK_sends_only_remaining_bytes_with_signed_payload_over_HTTP()
	{
		using var handler = new CaptureHandler();
		using var client = (AmazonS3Client)ValidOptions().CreateClient();
		((AmazonS3Config)client.Config).HttpClientFactory = new CaptureClientFactory(handler);
		using var input = new MemoryStream(Encoding.UTF8.GetBytes("skip:image"));
		input.Position = 5;
		var key = await Service(client).UploadAsync(input, "image/png");
		Assert.Equal("image", handler.Content);
		Assert.Equal("image/png", handler.ContentType);
		Assert.Equal($"/receipts/{key}", handler.Path);
		Assert.StartsWith("AWS4-HMAC-SHA256 ", handler.Authorization);
		Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("image"))), handler.PayloadHash);
		Assert.True(input.CanRead);
	}

	[Fact]
	public async Task Upload_uses_remaining_content_mime_bucket_unique_keys_and_leaves_input_open()
	{
		using var client = new RecordingClient();
		var service = Service(client);
		using var input = new MemoryStream(Encoding.UTF8.GetBytes("skip:image"));
		using var cancellation = new CancellationTokenSource();
		input.Position = 5;
		var key = await service.UploadAsync(input, "image/png", cancellation.Token);
		Assert.Matches("^receipts/[a-f0-9]{32}$", key);
		Assert.Equal("image", client.Content);
		Assert.Equal("image/png", client.Upload!.ContentType);
		Assert.Equal("receipts", client.Upload.BucketName);
		Assert.Equal(key, client.Upload.Key);
		Assert.False(client.Upload.AutoCloseStream);
		Assert.False(client.Upload.AutoResetStreamPosition);
		Assert.False(client.Upload.UseChunkEncoding);
		Assert.NotEqual(true, client.Upload.DisablePayloadSigning);
		Assert.Equal(cancellation.Token, client.Token);
		Assert.True(input.CanRead);
		input.Position = 5;
		Assert.NotEqual(key, await service.UploadAsync(input, "image/png"));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Nonseekable_upload_cleans_buffer_on_success_and_failure(bool fail)
	{
		using var client = new RecordingClient { Error = fail ? new AmazonS3Exception("synthetic failure") : null };
		using var input = new NonseekableStream(Encoding.UTF8.GetBytes("image"));
		if (fail)
			await Assert.ThrowsAsync<AmazonS3Exception>(() => Service(client).UploadAsync(input, "image/png"));
		else
			await Service(client).UploadAsync(input, "image/png");
		Assert.Equal("image", client.Content);
		Assert.True(input.CanRead);
		Assert.NotNull(client.TemporaryPath);
		Assert.False(File.Exists(client.TemporaryPath));
	}

	[Fact]
	public async Task Nonseekable_upload_cleans_buffer_when_cancelled_during_upload()
	{
		using var cancellation = new CancellationTokenSource();
		using var client = new RecordingClient { BeforeUploadCompletes = cancellation.Cancel };
		using var input = new NonseekableStream([1, 2, 3]);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(client).UploadAsync(input, "image/png", cancellation.Token));
		Assert.NotNull(client.TemporaryPath);
		Assert.False(File.Exists(client.TemporaryPath));
		Assert.True(input.CanRead);
	}

	[Fact]
	public async Task Upload_rejects_invalid_input_without_calling_storage()
	{
		using var client = new RecordingClient();
		var service = Service(client);
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.UploadAsync(null!, "image/png"));
		using var empty = new MemoryStream();
		await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(empty, "image/png"));
		using var exhausted = new MemoryStream([1]);
		exhausted.Position = 1;
		await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(exhausted, "image/png"));
		using var nonseekableEmpty = new NonseekableStream([]);
		await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(nonseekableEmpty, "image/png"));
		using var content = new MemoryStream([1]);
		await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(content, " "));
		using var closed = new MemoryStream([1]);
		closed.Close();
		await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(closed, "image/png"));
		Assert.Null(client.Upload);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Interrupted_copy_removes_partial_buffer_without_uploading_or_closing_source(bool cancelled)
	{
		using var client = new RecordingClient();
		using var input = new InterruptedReadStream(cancelled);
		using var cancellation = new CancellationTokenSource();
		var upload = Service(client).UploadAsync(input, "image/png", cancellation.Token);
		await input.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
		if (cancelled)
			cancellation.Cancel();
		var error = await Record.ExceptionAsync(() => upload.WaitAsync(TimeSpan.FromSeconds(5)));

		if (cancelled)
			Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
		else
			Assert.Same(input.Error, error);
		Assert.Equal(3, input.BytesRead);
		Assert.NotNull(input.TemporaryPath);
		Assert.False(File.Exists(input.TemporaryPath));
		Assert.True(input.CanRead);
		Assert.Null(client.Upload);
	}

	[Fact]
	public async Task Nonseekable_binary_payload_reaches_real_SDK_without_corruption()
	{
		byte[] bytes = [0x00, 0xFF, 0x80, 0x0D, 0x0A, 0x01];
		using var handler = new CaptureHandler();
		using var client = (AmazonS3Client)ValidOptions().CreateClient();
		((AmazonS3Config)client.Config).HttpClientFactory = new CaptureClientFactory(handler);
		using var input = new NonseekableStream(bytes);

		await Service(client).UploadAsync(input, "image/png");

		Assert.Equal(bytes, handler.Bytes);
		Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), handler.PayloadHash);
		Assert.True(input.CanRead);
	}

	[Fact]
	public async Task Cancellation_during_signing_does_not_return_a_url()
	{
		using var cancellation = new CancellationTokenSource();
		using var client = new RecordingClient { OnSign = cancellation.Cancel };

		var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => Service(client).GetUrlAsync("receipts/synthetic", cancellation.Token));

		Assert.Equal(cancellation.Token, error.CancellationToken);
		Assert.NotNull(client.Signed);
	}

	[Fact]
	public async Task Url_is_GET_with_matching_expiration_without_network_lookup()
	{
		using var client = new RecordingClient();
		var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(123);
		var service = new S3FileStorageService(client, Options.Create(ValidOptions()), new FixedTimeProvider(now));
		var result = await service.GetUrlAsync("receipts/key");
		Assert.Equal("http://127.0.0.1:8333/receipts/receipts/key?signed", result.Url);
		Assert.Equal(now.AddMilliseconds(-123).AddMinutes(15), result.ExpiresAt);
		Assert.Equal(result.ExpiresAt.UtcDateTime, client.Signed!.Expires);
		Assert.Equal(HttpVerb.GET, client.Signed.Verb);
		Assert.Equal(Protocol.HTTP, client.Signed.Protocol);
		Assert.Equal("receipts", client.Signed.BucketName);
		Assert.Equal("receipts/key", client.Signed.Key);
	}

	[Theory]
	[InlineData("http")]
	[InlineData("https")]
	public async Task Real_SDK_signs_local_path_style_GET(string scheme)
	{
		var options = ValidOptions();
		options.Endpoint = $"{scheme}://127.0.0.1:8333";
		using var client = options.CreateClient();
		var result = await new S3FileStorageService(client, Options.Create(options), TimeProvider.System).GetUrlAsync("receipts/synthetic");
		var uri = new Uri(result.Url);
		Assert.Equal($"{scheme}://127.0.0.1:8333/receipts/receipts/synthetic", uri.GetLeftPart(UriPartial.Path));
		Assert.Contains("X-Amz-Algorithm=AWS4-HMAC-SHA256", uri.Query);
		Assert.Contains("X-Amz-Signature=", uri.Query);
		Assert.Contains("us-east-1", uri.Query);
		Assert.InRange(result.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
	}

	[Theory]
	[InlineData("NoSuchKey", false)]
	[InlineData("NoSuchBucket", true)]
	[InlineData("AccessDenied", true)]
	[InlineData("InternalError", true)]
	public async Task Delete_only_swallows_missing_object(string code, bool throws)
	{
		using var client = new RecordingClient { Error = new AmazonS3Exception("synthetic") { ErrorCode = code, StatusCode = HttpStatusCode.NotFound } };
		using var cancellation = new CancellationTokenSource();
		if (throws)
			await Assert.ThrowsAsync<AmazonS3Exception>(() => Service(client).DeleteAsync("receipts/key", cancellation.Token));
		else
			await Service(client).DeleteAsync("receipts/key", cancellation.Token);
		Assert.Equal("receipts/key", client.Deleted!.Key);
		Assert.Equal("receipts", client.Deleted.BucketName);
		Assert.Equal(cancellation.Token, client.Token);
	}

	[Fact]
	public async Task Repeated_delete_succeeds_and_rejects_empty_keys()
	{
		using var client = new RecordingClient();
		var service = Service(client);
		await service.DeleteAsync("receipts/key");
		await service.DeleteAsync("receipts/key");
		await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(" "));
		await Assert.ThrowsAsync<ArgumentException>(() => service.GetUrlAsync(" "));
	}

	[Fact]
	public async Task Precancelled_operations_do_not_call_storage()
	{
		using var client = new RecordingClient();
		using var content = new MemoryStream([1]);
		var token = new CancellationToken(true);
		var service = Service(client);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UploadAsync(content, "image/png", token));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetUrlAsync("key", token));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DeleteAsync("key", token));
		Assert.Null(client.Upload);
		Assert.Null(client.Signed);
		Assert.Null(client.Deleted);
	}

	private static S3FileStorageService Service(IAmazonS3 client) => new(client, Options.Create(ValidOptions()), TimeProvider.System);

	internal static S3StorageOptions ValidOptions() => new()
	{
		Endpoint = "http://127.0.0.1:8333",
		Bucket = "receipts",
		Region = "us-east-1",
		AccessKey = "synthetic-access",
		SecretKey = "synthetic-secret"
	};

	private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private sealed class CaptureClientFactory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
	{
		public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
		public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;
	}

	private sealed class CaptureHandler : HttpMessageHandler
	{
		public string? Content { get; private set; }
		public byte[]? Bytes { get; private set; }
		public string? ContentType { get; private set; }
		public string? Path { get; private set; }
		public string? Authorization { get; private set; }
		public string? PayloadHash { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
			Content = Encoding.UTF8.GetString(Bytes);
			ContentType = request.Content.Headers.ContentType?.MediaType;
			Path = request.RequestUri!.AbsolutePath;
			Authorization = request.Headers.GetValues("Authorization").Single();
			PayloadHash = request.Headers.GetValues("x-amz-content-sha256").Single();
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
		}
	}

	private sealed class RecordingClient() : AmazonS3Client(new BasicAWSCredentials("synthetic", "synthetic"), new AmazonS3Config { ServiceURL = "http://127.0.0.1:8333" }), IAmazonS3
	{
		public PutObjectRequest? Upload { get; private set; }
		public DeleteObjectRequest? Deleted { get; private set; }
		public GetPreSignedUrlRequest? Signed { get; private set; }
		public string? Content { get; private set; }
		public string? TemporaryPath { get; private set; }
		public CancellationToken Token { get; private set; }
		public Exception? Error { get; init; }
		public Action? BeforeUploadCompletes { get; init; }
		public Action? OnSign { get; init; }

		public override async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
		{
			Upload = request;
			Token = cancellationToken;
			TemporaryPath = (request.InputStream as FileStream)?.Name;
			using var reader = new StreamReader(request.InputStream, leaveOpen: true);
			Content = await reader.ReadToEndAsync(cancellationToken);
			BeforeUploadCompletes?.Invoke();
			cancellationToken.ThrowIfCancellationRequested();
			if (Error is not null)
				throw Error;
			return new PutObjectResponse();
		}

		public new Task<string> GetPreSignedURLAsync(GetPreSignedUrlRequest request)
		{
			Signed = request;
			OnSign?.Invoke();
			return Task.FromResult("http://127.0.0.1:8333/receipts/receipts/key?signed");
		}

		public override Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
		{
			Deleted = request;
			Token = cancellationToken;
			if (Error is not null)
				throw Error;
			return Task.FromResult(new DeleteObjectResponse());
		}
	}

	private sealed class NonseekableStream(byte[] bytes) : Stream
	{
		private readonly MemoryStream _inner = new(bytes);
		public override bool CanRead => _inner.CanRead;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
		public override void Flush() => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_inner.Dispose();
			base.Dispose(disposing);
		}
	}
}
