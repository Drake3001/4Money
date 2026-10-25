using ZPI4M.Receipt.DTOs;

namespace ZPI4M.Receipt.Services;

public interface IReceiptService
{
	Task<Guid> CreateAsync(
		Stream content,
		string contentType,
		CancellationToken cancellationToken = default);

	Task<Models.Receipt?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken = default);

	Task<FileUrlResult> GetFileUrlAsync(Guid receiptId, CancellationToken cancellationToken = default);

	Task DeleteAsync(Guid receiptId, CancellationToken cancellationToken = default);
}
