using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Receipt.Services;

namespace ZPI4M.Receipt.UnitTests;

public sealed class HttpReceiptAnalysisServiceTests
{
	internal const string ResponseJson = """
		{"receipt":{"store":null,"total":"6.00","items":[{"name":null}]},"warnings":[{"code":"CHECK","message":"Synthetic warning"}],"needs_review":true,"extra":{"value":42}}
		""";

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Sends_remaining_bytes_as_single_file_and_preserves_full_response(bool nonSeekable)
	{
		using var source = new MemoryStream(Encoding.UTF8.GetBytes("skip:synthetic image"));
		source.Position = 5;
		using Stream input = nonSeekable ? new NonSeekableStream(source) : source;
		using var handler = new Handler(async (request, token) =>
		{
			Assert.Equal(HttpMethod.Post, request.Method);
			Assert.Equal("http://ocr.example.test/api/v1/extract", request.RequestUri!.AbsoluteUri);
			Assert.Null(request.Headers.Authorization);
			Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
			var file = Assert.Single(Assert.IsType<MultipartFormDataContent>(request.Content));
			Assert.Equal("files", file.Headers.ContentDisposition!.Name!.Trim('"'));
			Assert.Equal("receipt.bin", file.Headers.ContentDisposition.FileName!.Trim('"'));
			Assert.Equal("image/png", file.Headers.ContentType!.MediaType);
			Assert.Equal("synthetic image", await file.ReadAsStringAsync(token));
			return JsonResponse(ResponseJson);
		});
		using var client = Client(handler);
		var result = await Service(client).AnalyzeAsync(input, "image/png");
		Assert.Equal(ResponseJson, result.GetRawText());
		Assert.True(result.GetProperty("needs_review").GetBoolean());
		Assert.Equal(JsonValueKind.Null, result.GetProperty("receipt").GetProperty("store").ValueKind);
		Assert.Equal(42, result.GetProperty("extra").GetProperty("value").GetInt32());
		Assert.True(input.CanRead);
		Assert.True(source.CanRead);
		Assert.Equal(1, handler.Calls);
	}

	[Theory]
	[InlineData("")]
	[InlineData("sensitive malformed JSON")]
	[InlineData("null")]
	[InlineData("[]")]
	[InlineData("{}")]
	[InlineData("{\"receipt\":null,\"warnings\":[],\"needs_review\":false}")]
	[InlineData("{\"receipt\":{},\"warnings\":{},\"needs_review\":false}")]
	[InlineData("{\"receipt\":{},\"warnings\":[],\"needs_review\":\"false\"}")]
	[InlineData("{\"warnings\":[],\"needs_review\":false}")]
	[InlineData("{\"receipt\":{},\"needs_review\":false}")]
	[InlineData("{\"receipt\":{},\"warnings\":[]}")]
	public async Task Rejects_invalid_response_without_exposing_body(string json)
	{
		using var handler = new Handler((_, _) => Task.FromResult(JsonResponse(json)));
		using var client = Client(handler);
		using var input = new MemoryStream([1]);
		var error = await Assert.ThrowsAsync<ReceiptAnalysisException>(() => Service(client).AnalyzeAsync(input, "image/png"));
		Assert.Equal(ReceiptAnalysisErrorKind.InvalidResponse, error.Kind);
		AssertSafeError(error);
		Assert.True(input.CanRead);
		Assert.Equal(1, handler.Calls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Multipart_serialization_preserves_binary_payload(bool nonSeekable)
	{
		byte[] bytes = [0x00, 0xFF, 0x80, 0x0D, 0x0A, 0x01];
		using var source = new MemoryStream(bytes);
		using Stream input = nonSeekable ? new NonSeekableStream(source) : source;
		using var handler = new Handler(async (request, token) =>
		{
			using var wire = new MemoryStream();
			await request.Content!.CopyToAsync(wire, token);
			var serialized = wire.ToArray();
			var headerEnd = serialized.AsSpan().IndexOf("\r\n\r\n"u8);
			Assert.True(headerEnd >= 0);
			Assert.Equal(bytes, serialized.AsSpan(headerEnd + 4, bytes.Length).ToArray());
			Assert.Equal((byte)'\r', serialized[headerEnd + 4 + bytes.Length]);
			return JsonResponse(ResponseJson);
		});
		using var client = Client(handler);

		await Service(client).AnalyzeAsync(input, "image/png");

		Assert.True(input.CanRead);
		Assert.Equal(1, handler.Calls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Interrupted_multipart_upload_preserves_source_and_sanitizes_transport_errors(bool cancelled)
	{
		using var input = new InterruptedReadStream(cancelled);
		using var cancellation = new CancellationTokenSource();
		using var handler = new Handler(async (request, token) =>
		{
			await request.Content!.CopyToAsync(Stream.Null, token);
			throw new InvalidOperationException("Unexpected completion.");
		});
		using var client = Client(handler);
		var analysis = Service(client).AnalyzeAsync(input, "image/png", cancellation.Token);
		await input.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
		if (cancelled)
			cancellation.Cancel();
		var error = await Record.ExceptionAsync(() => analysis.WaitAsync(TimeSpan.FromSeconds(5)));

		if (cancelled)
			Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
		else
		{
			var transport = Assert.IsType<ReceiptAnalysisException>(error);
			Assert.Equal(ReceiptAnalysisErrorKind.TransportError, transport.Kind);
			AssertSafeError(transport);
		}
		Assert.Equal(3, input.BytesRead);
		Assert.True(input.CanRead);
		Assert.Equal(1, handler.Calls);
	}

	[Fact]
	public async Task Accepts_incomplete_nested_data()
	{
		using var handler = new Handler((_, _) => Task.FromResult(JsonResponse("{\"receipt\":{},\"warnings\":[null],\"needs_review\":false}")));
		using var client = Client(handler);
		using var input = new MemoryStream([1]);
		var result = await Service(client).AnalyzeAsync(input, "image/png");
		Assert.False(result.GetProperty("needs_review").GetBoolean());
	}

	[Theory]
	[InlineData(422)]
	[InlineData(500)]
	[InlineData(302)]
	[InlineData(201)]
	[InlineData(204)]
	public async Task Non_200_status_is_not_retried_or_exposed(int status)
	{
		using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
		{
			Content = new StringContent("sensitive error body"),
			ReasonPhrase = "sensitive reason"
		}));
		using var client = Client(handler);
		using var input = new MemoryStream([1]);
		var error = await Assert.ThrowsAsync<ReceiptAnalysisException>(() => Service(client).AnalyzeAsync(input, "image/png"));
		Assert.Equal(ReceiptAnalysisErrorKind.HttpError, error.Kind);
		Assert.Equal((HttpStatusCode)status, error.StatusCode);
		AssertSafeError(error);
		Assert.True(input.CanRead);
		Assert.Equal(1, handler.Calls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Transport_failures_are_sanitized(bool duringBodyRead)
	{
		using var handler = new Handler((_, _) => duringBodyRead
			? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenReadStream()) })
			: throw new HttpRequestException("sensitive transport details"));
		using var client = Client(handler);
		using var input = new MemoryStream([1]);
		var error = await Assert.ThrowsAsync<ReceiptAnalysisException>(() => Service(client).AnalyzeAsync(input, "image/png"));
		Assert.Equal(ReceiptAnalysisErrorKind.TransportError, error.Kind);
		AssertSafeError(error);
		Assert.True(input.CanRead);
		Assert.Equal(1, handler.Calls);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task Cancellation_and_timeout_cover_send_and_response_body(bool callerCancels, bool duringBodyRead)
	{
		using var cancellation = new CancellationTokenSource();
		using var handler = new Handler(async (_, token) =>
		{
			if (duringBodyRead)
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new WaitingReadStream(callerCancels ? cancellation : null)) };
			if (callerCancels)
				cancellation.Cancel();
			await Task.Delay(Timeout.Infinite, token);
			throw new InvalidOperationException("Unexpected completion.");
		});
		using var client = Client(handler);
		using var input = new MemoryStream([1]);
		var service = Service(client, 1);
		if (callerCancels)
		{
			var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeAsync(input, "image/png", cancellation.Token));
			Assert.Equal(cancellation.Token, error.CancellationToken);
		}
		else
		{
			var error = await Assert.ThrowsAsync<ReceiptAnalysisException>(() => service.AnalyzeAsync(input, "image/png"));
			Assert.Equal(ReceiptAnalysisErrorKind.Timeout, error.Kind);
			AssertSafeError(error);
		}
		Assert.True(input.CanRead);
		Assert.Equal(1, handler.Calls);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("invalid MIME")]
	public async Task Invalid_content_type_does_not_send_request(string? mime)
	{
		using var handler = new Handler((_, _) => throw new InvalidOperationException());
		using var client = Client(handler);
		using var input = new MemoryStream([1]);
		await Assert.ThrowsAnyAsync<ArgumentException>(() => Service(client).AnalyzeAsync(input, mime!));
		Assert.Equal(0, handler.Calls);
		Assert.Equal(0, input.Position);
	}

	[Fact]
	public async Task Null_unreadable_and_precancelled_inputs_do_not_send()
	{
		using var handler = new Handler((_, _) => throw new InvalidOperationException());
		using var client = Client(handler);
		var service = Service(client);
		await Assert.ThrowsAsync<ArgumentNullException>(() => service.AnalyzeAsync(null!, "image/png"));
		using var unreadable = new MemoryStream([1]);
		unreadable.Close();
		await Assert.ThrowsAsync<ArgumentException>(() => service.AnalyzeAsync(unreadable, "image/png"));
		using var input = new MemoryStream([1]);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeAsync(input, "image/png", cancellation.Token));
		Assert.Equal(0, input.Position);
		Assert.Equal(0, handler.Calls);
	}

	internal static HttpClient Client(HttpMessageHandler handler) => new(handler)
	{
		BaseAddress = new Uri("http://ocr.example.test"),
		Timeout = Timeout.InfiniteTimeSpan
	};

	internal static HttpReceiptAnalysisService Service(HttpClient client, int timeoutSeconds = 120)
		=> new(client, Options.Create(new ReceiptAnalysisOptions { TimeoutSeconds = timeoutSeconds }));

	internal static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
	{
		Content = new StringContent(json, Encoding.UTF8, "application/json")
	};

	private static void AssertSafeError(ReceiptAnalysisException error)
	{
		Assert.Null(error.InnerException);
		Assert.DoesNotContain("sensitive", error.ToString());
	}

	internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
	{
		public int Calls { get; private set; }
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Calls++;
			return send(request, cancellationToken);
		}
	}

	private class NonSeekableStream(Stream source) : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => source.ReadAsync(buffer, cancellationToken);
		public override void Flush() => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	private sealed class BrokenReadStream() : NonSeekableStream(Stream.Null)
	{
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
			=> throw new IOException("sensitive body read error");
	}

	private sealed class WaitingReadStream(CancellationTokenSource? callerCancellation) : NonSeekableStream(Stream.Null)
	{
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			callerCancellation?.Cancel();
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return 0;
		}
	}
}
