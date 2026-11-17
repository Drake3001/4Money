using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using ZPI4M.Receipt.Services;
using ZPI4M.Shared.Storage;

namespace ZPI4M.Expense.IntegrationTests;

internal sealed class ReceiptTestDependencies : IObjectStorage, IReceiptAnalysisService
{
	private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _files = new ConcurrentDictionary<string, (byte[], string)>();

	private int _analysisCalls;

	public ReceiptAnalysisErrorKind? AnalysisError { get; init; }

	public HttpStatusCode? AnalysisStatus { get; init; }

	public bool EmptyAnalysis { get; init; }
	public string? AnalysisJson { get; init; }

	public Func<Task>? BeforeUploadAsync { get; init; }

	public int AnalysisCalls => Volatile.Read(ref _analysisCalls);

	public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken cancellationToken = default)
	{
		return Task.FromResult((IReadOnlyList<string>)_files.Keys.Where((string key) => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray());
	}

	public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
	{
		if (BeforeUploadAsync is not null) await BeforeUploadAsync();
		using MemoryStream copy = new MemoryStream();
		await content.CopyToAsync(copy, cancellationToken);
		_files[key] = (copy.ToArray(), contentType);
	}

	public Task<FileUrlResult> GetReadUrlAsync(string key, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(new FileUrlResult("https://storage.example/" + key, DateTimeOffset.UtcNow.AddMinutes(10.0)));
	}

	public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		_files.TryRemove(key, out (byte[], string) _);
		return Task.CompletedTask;
	}

	public Task<JsonElement> AnalyzeAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Interlocked.Increment(ref _analysisCalls);
		ReceiptAnalysisErrorKind? analysisError = AnalysisError;
		if (analysisError.HasValue)
		{
			ReceiptAnalysisErrorKind valueOrDefault = analysisError.GetValueOrDefault();
			throw new ReceiptAnalysisException(valueOrDefault, AnalysisStatus);
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(AnalysisJson ?? (EmptyAnalysis ? "{\"receipt\":{},\"warnings\":[],\"needs_review\":true}" : "{\n  \"receipt\": {\n    \"store\": \"Synthetic shop\", \"date\": \"2026-11-02\", \"currency\": \"PLN\", \"amount_due\": \"100.00\",\n    \"items\": [{\"name\":\"Milk\",\"name_corrected\":\"Milk\",\"unit_price\":\"70.00\",\"quantity\":\"1\",\"category\":\"unmapped\",\"needs_review\":true,\"review_reason\":\"ambiguous_category\"}]\n  },\n  \"warnings\": [{\"code\":\"review\",\"message\":\"Check the item\",\"field\":\"items[0]\"}], \"needs_review\": true\n}"));
		return Task.FromResult(jsonDocument.RootElement.Clone());
	}
}
