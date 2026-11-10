using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ZPI4M.Receipt.Database;
using ZPI4M.Receipt.DTOs;
using ZPI4M.Receipt.Repositories.ReceiptRepository;
using ZPI4M.Shared.Storage;

namespace ZPI4M.Receipt.Services;

public sealed class ReceiptService : IReceiptService
{
	public const int MaximumFileBytes = 20 * 1024 * 1024;

	private readonly IReceiptRepository _repository;
	private readonly ReceiptDbContext _context;
	private readonly IReceiptAnalysisService _analysis;
	private readonly IObjectStorage _storage;
	private readonly ILogger<ReceiptService> _logger;
	private readonly TimeProvider _timeProvider;

	public ReceiptService(
		IReceiptRepository repository,
		ReceiptDbContext context,
		IReceiptAnalysisService analysis,
		IObjectStorage storage,
		ILogger<ReceiptService> logger,
		TimeProvider timeProvider)
	{
		_repository = repository;
		_context = context;
		_analysis = analysis;
		_storage = storage;
		_logger = logger;
		_timeProvider = timeProvider;
	}

	public async Task<ReceiptCreationResult> CreateAsync(Stream content, string contentType, bool runOcr, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
		if (!content.CanRead)
			throw new ArgumentException("The content stream must be readable.", nameof(content));
		cancellationToken.ThrowIfCancellationRequested();

		await using var copy = CreateTemporaryStream();
		await content.CopyToAsync(copy, cancellationToken);
		if (copy.Length == 0)
			throw new ArgumentException("The content stream must not be empty.", nameof(content));

		copy.Position = 0;
		JsonElement? result = runOcr ? await _analysis.AnalyzeAsync(copy, contentType, cancellationToken) : null;
		var extractionResult = result?.GetRawText();
		copy.Position = 0;
		var storageKey = $"receipts/{Guid.NewGuid():N}";
		await _storage.UploadAsync(storageKey, copy, contentType, cancellationToken);

		var receipt = new Models.Receipt
		{
			Id = Guid.NewGuid(),
			StorageKey = storageKey,
			ExtractionResult = extractionResult
		};
		try
		{
			_repository.Add(receipt);
			await _context.SaveChangesAsync(cancellationToken);
			return new ReceiptCreationResult(receipt.Id, result);
		}
		catch (Exception error)
		{
			_context.Entry(receipt).State = EntityState.Detached;
			// A failed acknowledgement does not prove the database write was rolled back.
			// Retain the uploaded image rather than risk deleting a persisted receipt's file.
			_logger.LogWarning("Receipt {ReceiptId}: persistence failed; uploaded file retained ({ErrorType}).", receipt.Id, error.GetType().Name);
			throw;
		}
	}

	public async Task<JsonElement> AnalyzeAsync(Stream content, string contentType, CancellationToken cancellationToken)
	{
		var result = await _analysis.AnalyzeAsync(content, contentType, cancellationToken);
		if (!HasUsableData(result))
			throw new ReceiptAnalysisException(ReceiptAnalysisErrorKind.NoUsableData);
		return result;
	}

	public async Task<Models.Receipt> UploadForExpenseAsync(Stream content, string contentType, string? extractionResult,
		CancellationToken cancellationToken)
	{
		var receipt = new Models.Receipt
		{
			Id = Guid.NewGuid(),
			StorageKey = $"receipts/{Guid.NewGuid():N}",
			ExtractionResult = extractionResult
		};
		try
		{
			await _storage.UploadAsync(receipt.StorageKey, content, contentType, cancellationToken);
			return receipt;
		}
		catch
		{
			await DeleteFileAsync(receipt);
			throw;
		}
	}

	public async Task InsertAsync(Models.Receipt receipt, CancellationToken cancellationToken)
	{
		_repository.Add(receipt);
		await _context.SaveChangesAsync(cancellationToken);
	}

	public async Task RemoveRecordAsync(Models.Receipt receipt, CancellationToken cancellationToken)
	{
		_repository.Remove(receipt);
		await _context.SaveChangesAsync(cancellationToken);
	}

	public Task<FileUrlResult> GetFileUrlAsync(Models.Receipt receipt, CancellationToken cancellationToken) =>
		_storage.GetReadUrlAsync(receipt.StorageKey, cancellationToken);

	public Task DeleteFileAsync(Models.Receipt receipt) => TryDeleteFileAsync(receipt);

	private static bool HasUsableData(JsonElement result)
	{
		if (result.ValueKind != JsonValueKind.Object ||
			!result.TryGetProperty("receipt", out var receipt) || receipt.ValueKind != JsonValueKind.Object)
			return false;
		if (HasText(receipt, "store") || HasDate(receipt) || HasNumber(receipt, "amount_due") || HasNumber(receipt, "total"))
			return true;
		if (!receipt.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
			return false;
		return items.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Object &&
			(HasText(item, "name") || HasText(item, "name_corrected") || HasNumber(item, "unit_price")));
	}

	private static bool HasDate(JsonElement receipt)
	{
		if (!receipt.TryGetProperty("date", out var date) || date.ValueKind != JsonValueKind.String)
			return false;
		return DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.None, out _);
	}

	private static bool HasText(JsonElement value, string property)
	{
		return value.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String &&
			!string.IsNullOrWhiteSpace(text.GetString());
	}

	private static bool HasNumber(JsonElement value, string property)
	{
		if (!value.TryGetProperty(property, out var number))
			return false;
		if (number.ValueKind == JsonValueKind.Number)
			return number.TryGetDecimal(out _);
		return number.ValueKind == JsonValueKind.String && decimal.TryParse(number.GetString(),
			System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
			System.Globalization.CultureInfo.InvariantCulture, out _);
	}

	private static FileStream CreateTemporaryStream()
	{
		return new FileStream(Path.Combine(Path.GetTempPath(), $"zpi4m-receipt-{Guid.NewGuid():N}.tmp"), new FileStreamOptions
		{
			Mode = FileMode.CreateNew,
			Access = FileAccess.ReadWrite,
			Share = FileShare.None,
			Options = (FileOptions.Asynchronous | FileOptions.DeleteOnClose)
		});
	}

	public Task<Models.Receipt?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken = default)
	{
		return _repository.GetByIdAsync(receiptId, cancellationToken);
	}

	public async Task DeleteAsync(Guid receiptId, CancellationToken cancellationToken = default)
	{
		var receipt = await _repository.GetByIdAsync(receiptId, cancellationToken);
		if (receipt is null)
			return;

		var previousState = _context.Entry(receipt).State;
		_repository.Remove(receipt);
		try
		{
			await _context.SaveChangesAsync(cancellationToken);
		}
		catch
		{
			_context.Entry(receipt).State = previousState;
			throw;
		}
		await TryDeleteFileAsync(receipt);
	}

	private async Task TryDeleteFileAsync(Models.Receipt receipt)
	{
		using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10), _timeProvider);
		try
		{
			await _storage.DeleteAsync(receipt.StorageKey, cleanup.Token);
		}
		catch (Exception error)
		{
			_logger.LogWarning("Receipt {ReceiptId}: file cleanup failed ({ErrorType}).", receipt.Id, error.GetType().Name);
		}
	}
}
