using Microsoft.EntityFrameworkCore;
using ZPI4M.Receipt.Database;

namespace ZPI4M.Receipt.Repositories.ReceiptRepository;

public class ReceiptRepository : IReceiptRepository
{
	private readonly ReceiptDbContext _context;

	public ReceiptRepository(ReceiptDbContext context)
	{
		_context = context;
	}

	public void Add(Models.Receipt entity)
	{
		_context.Receipts.Add(entity);
	}

	public void Remove(Models.Receipt entity)
	{
		_context.Receipts.Remove(entity);
	}

	public Task<Models.Receipt?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken = default)
	{
		return _context.Receipts.SingleOrDefaultAsync(r => r.Id == receiptId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid receiptId, CancellationToken cancellationToken = default)
	{
		return _context.Receipts.AnyAsync(r => r.Id == receiptId, cancellationToken);
	}
}
