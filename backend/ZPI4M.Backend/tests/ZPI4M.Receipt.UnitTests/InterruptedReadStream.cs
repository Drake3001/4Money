namespace ZPI4M.Receipt.UnitTests;

// Delivers a partial binary payload, then fails or waits for real token cancellation.
internal sealed class InterruptedReadStream(bool waitForCancellation) : Stream
{
	private bool _read;
	private bool _disposed;
	public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public IOException Error { get; } = new("sensitive partial read failure");
	public int BytesRead { get; private set; }
	public string? TemporaryPath { get; private set; }
	public override bool CanRead => !_disposed;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		if (!_read)
		{
			_read = true;
			byte[] bytes = [0x00, 0xFF, 0x7F];
			bytes.AsMemory().CopyTo(buffer);
			BytesRead = bytes.Length;
			return bytes.Length;
		}
		Waiting.TrySetResult();
		if (waitForCancellation)
			await Task.Delay(Timeout.Infinite, cancellationToken);
		throw Error;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		=> ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
	{
		TemporaryPath = (destination as FileStream)?.Name;
		return base.CopyToAsync(destination, bufferSize, cancellationToken);
	}

	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void Flush() => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	protected override void Dispose(bool disposing)
	{
		_disposed = true;
		base.Dispose(disposing);
	}
}
