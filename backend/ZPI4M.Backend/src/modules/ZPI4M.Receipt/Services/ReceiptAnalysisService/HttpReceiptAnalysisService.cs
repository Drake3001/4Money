using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ZPI4M.Receipt.Services;

public sealed class HttpReceiptAnalysisService : IReceiptAnalysisService
{
	private readonly HttpClient _client;
	private readonly TimeSpan _timeout;

	public HttpReceiptAnalysisService(HttpClient client, IOptions<ReceiptAnalysisOptions> options)
	{
		_client = client;
		_timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
	}

	public async Task<JsonElement> AnalyzeAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
		if (!content.CanRead)
			throw new ArgumentException("The content stream must be readable.", nameof(content));
		if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
			throw new ArgumentException("The content type must be a valid MIME type.", nameof(contentType));
		cancellationToken.ThrowIfCancellationRequested();

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_timeout);
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/extract");
		using var multipart = new MultipartFormDataContent();
		using var file = new StreamContent(new LeaveOpenReadStream(content));
		file.Headers.ContentType = mediaType;
		multipart.Add(file, "files", "receipt.bin");
		request.Content = multipart;
		try
		{
			using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			if (response.StatusCode != HttpStatusCode.OK)
				throw new ReceiptAnalysisException(ReceiptAnalysisErrorKind.HttpError, response.StatusCode);
			await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
			using var document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
			timeout.Token.ThrowIfCancellationRequested();
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("receipt", out var receipt) || receipt.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("warnings", out var warnings) || warnings.ValueKind != JsonValueKind.Array
				|| !root.TryGetProperty("needs_review", out var review)
				|| (review.ValueKind != JsonValueKind.True && review.ValueKind != JsonValueKind.False))
				throw new ReceiptAnalysisException(ReceiptAnalysisErrorKind.InvalidResponse);
			return root.Clone();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw new OperationCanceledException(cancellationToken);
		}
		catch (OperationCanceledException) when (timeout.IsCancellationRequested)
		{
			throw new ReceiptAnalysisException(ReceiptAnalysisErrorKind.Timeout);
		}
		catch (JsonException)
		{
			throw new ReceiptAnalysisException(ReceiptAnalysisErrorKind.InvalidResponse);
		}
		catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
		{
			throw new ReceiptAnalysisException(ReceiptAnalysisErrorKind.TransportError);
		}
	}

	// StreamContent owns this wrapper, while the caller retains ownership of the source.
	private sealed class LeaveOpenReadStream(Stream source) : Stream
	{
		public override bool CanRead => source.CanRead;
		public override bool CanSeek => source.CanSeek;
		public override bool CanWrite => false;
		public override long Length => source.Length;
		public override long Position { get => source.Position; set => source.Position = value; }
		public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
			=> source.ReadAsync(buffer, offset, count, cancellationToken);
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
			=> source.ReadAsync(buffer, cancellationToken);
		public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
		public override void Flush() => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
