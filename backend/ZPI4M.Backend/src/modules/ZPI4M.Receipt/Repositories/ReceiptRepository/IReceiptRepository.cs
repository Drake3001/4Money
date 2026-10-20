namespace ZPI4M.Receipt.Repositories.ReceiptRepository;

public interface IReceiptRepository
{
	void Add(Models.Receipt entity);
	void Remove(Models.Receipt entity);
	Task<Models.Receipt?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid receiptId, CancellationToken cancellationToken = default);
}
