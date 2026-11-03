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

	Task<Models.Receipt?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken = default);

	Task<FileUrlResult> GetFileUrlByIdAsync(Guid receiptId, CancellationToken cancellationToken = default);

	Task DeleteAsync(Guid receiptId, CancellationToken cancellationToken = default);
}
