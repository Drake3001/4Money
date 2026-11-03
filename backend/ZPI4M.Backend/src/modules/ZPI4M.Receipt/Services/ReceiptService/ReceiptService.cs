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

		var temporaryPath = Path.Combine(Path.GetTempPath(), $"zpi4m-receipt-{Guid.NewGuid():N}.tmp");
		await using var copy = new FileStream(temporaryPath, new FileStreamOptions
		{
			Mode = FileMode.CreateNew,
			Access = FileAccess.ReadWrite,
			Share = FileShare.None,
			Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
		});
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

	public Task<Models.Receipt?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken = default)
	{
		return _repository.GetByIdAsync(receiptId, cancellationToken);
	}

	public async Task<FileUrlResult> GetFileUrlByIdAsync(Guid receiptId, CancellationToken cancellationToken = default)
	{
		var receipt = await _repository.GetByIdAsync(receiptId, cancellationToken)
			?? throw new KeyNotFoundException("Receipt not found.");
		return await _storage.GetReadUrlAsync(receipt.StorageKey, cancellationToken);
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
