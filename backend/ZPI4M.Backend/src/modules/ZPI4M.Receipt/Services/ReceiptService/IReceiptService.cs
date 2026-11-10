using System.Text.Json;
using ZPI4M.Receipt.DTOs;
using ZPI4M.Shared.Storage;

namespace ZPI4M.Receipt.Services;

public interface IReceiptService
{
	Task<ReceiptCreationResult> CreateAsync(
		Stream content,
		string contentType,
		bool runOcr,
		CancellationToken cancellationToken = default);

	Task<JsonElement> AnalyzeAsync(Stream content, string contentType, CancellationToken cancellationToken);
	Task<Models.Receipt> UploadForExpenseAsync(Stream content, string contentType, string? extractionResult, CancellationToken cancellationToken);
	Task InsertAsync(Models.Receipt receipt, CancellationToken cancellationToken);
	Task RemoveRecordAsync(Models.Receipt receipt, CancellationToken cancellationToken);
	Task DeleteFileAsync(Models.Receipt receipt);
	Task<FileUrlResult> GetFileUrlAsync(Models.Receipt receipt, CancellationToken cancellationToken);

	Task<Models.Receipt?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken = default);

	Task DeleteAsync(Guid receiptId, CancellationToken cancellationToken = default);
}
