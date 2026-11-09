using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Repositories.BankTransactionRepository;

public class BankTransactionRepository : IBankTransactionRepository
{
	private readonly BankingDbContext _context;

	public BankTransactionRepository(BankingDbContext context)
	{
		_context = context;
	}

	public void Add(BankTransaction entity)
	{
		_context.BankTransactions.Add(entity);
	}

	public void Remove(BankTransaction entity)
	{
		_context.BankTransactions.Remove(entity);
	}

	public Task<BankTransaction?> GetByIdAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
	{
		return _context.BankTransactions.SingleOrDefaultAsync(e => e.Id == bankTransactionId, cancellationToken);
	}

	public Task<BankTransaction?> GetByIdForUserAsync(Guid userId, Guid bankTransactionId, CancellationToken cancellationToken = default)
	{
		return _context.BankTransactions.AsNoTracking().SingleOrDefaultAsync(
			e => e.Id == bankTransactionId && e.FkBankAccount.FkUserId == userId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid bankTransactionId, CancellationToken cancellationToken = default)
	{
		return _context.BankTransactions.AnyAsync(e => e.Id == bankTransactionId, cancellationToken);
	}

	public Task<BankTransaction?> GetByTinkTransactionIdAsync(string tinkTransactionId, CancellationToken cancellationToken = default)
	{
		return _context.BankTransactions.SingleOrDefaultAsync(t => t.TinkTransactionId == tinkTransactionId, cancellationToken);
	}

	public Task<bool> ExistsByTinkTransactionIdAsync(string tinkTransactionId, CancellationToken cancellationToken = default)
	{
		return _context.BankTransactions.AnyAsync(t => t.TinkTransactionId == tinkTransactionId, cancellationToken);
	}

	public void AddRange(IEnumerable<BankTransaction> transactions)
	{
		_context.BankTransactions.AddRange(transactions);
	}

	public async Task<PagedResult<BankTransaction>> GetPageAsync(BankTransactionQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (query.DateFrom > query.DateTo)
			throw new ArgumentException("DateFrom must be earlier than or equal to DateTo.", nameof(query));
		if (query.MinAmount > query.MaxAmount)
			throw new ArgumentException("MinAmount must be less than or equal to MaxAmount.", nameof(query));

		IQueryable<BankTransaction> itemsQuery = _context.BankTransactions.AsNoTracking();
		if (query.BankAccountId.HasValue)
			itemsQuery = itemsQuery.Where(e => e.FkBankAccountId == query.BankAccountId.Value);
		if (query.UserId.HasValue)
			itemsQuery = itemsQuery.Where(e => e.FkBankAccount.FkUserId == query.UserId.Value);
		if (query.SettlementAssignment.HasValue)
			itemsQuery = itemsQuery.Where(e => _context.BankTransactionAssignments.Any(a =>
				a.BankTransactionId == e.Id && a.ConsumerType == BankTransactionConsumerType.GroupSettlement) == query.SettlementAssignment.Value);
		if (query.ExpenseAssignment.HasValue)
			itemsQuery = itemsQuery.Where(e => _context.BankTransactionAssignments.Any(a =>
				a.BankTransactionId == e.Id && a.ConsumerType == BankTransactionConsumerType.Expense) == query.ExpenseAssignment.Value);
		if (!string.IsNullOrWhiteSpace(query.Status))
		{
			var status = query.Status.Trim();
			itemsQuery = itemsQuery.Where(e => e.Status == status);
		}
		if (!string.IsNullOrWhiteSpace(query.Description))
		{
			var description = query.Description.Trim().ToLowerInvariant();
			itemsQuery = itemsQuery.Where(e => e.Description != null && e.Description.ToLower().Contains(description));
		}
		if (!string.IsNullOrWhiteSpace(query.CurrencyCode))
		{
			var currencyCode = query.CurrencyCode.Trim().ToUpperInvariant();
			itemsQuery = itemsQuery.Where(e => e.CurrencyCode.ToUpper() == currencyCode);
		}
		if (query.DateFrom.HasValue)
			itemsQuery = itemsQuery.Where(e => e.Date >= query.DateFrom.Value);
		if (query.DateTo.HasValue)
			itemsQuery = itemsQuery.Where(e => e.Date <= query.DateTo.Value);
		if (query.MinAmount.HasValue)
			itemsQuery = itemsQuery.Where(e => e.Amount >= query.MinAmount.Value);
		if (query.MaxAmount.HasValue)
			itemsQuery = itemsQuery.Where(e => e.Amount <= query.MaxAmount.Value);

		IOrderedQueryable<BankTransaction> ordered;
		switch (query.SortBy)
		{
			case BankTransactionSortBy.Date:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.Date);
				else
					ordered = itemsQuery.OrderBy(e => e.Date);
				break;
			case BankTransactionSortBy.Amount:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.Amount);
				else
					ordered = itemsQuery.OrderBy(e => e.Amount);
				break;
			case BankTransactionSortBy.Status:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.Status);
				else
					ordered = itemsQuery.OrderBy(e => e.Status);
				break;
			case BankTransactionSortBy.CurrencyCode:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.CurrencyCode);
				else
					ordered = itemsQuery.OrderBy(e => e.CurrencyCode);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(query), "Unknown sort field.");
		}

		int offset = query.GetOffset();
		int totalCount = await itemsQuery.CountAsync(cancellationToken);
		var items = await ordered.ThenBy(e => e.Id)
			.Skip(offset)
			.Take(query.PageSize)
			.ToListAsync(cancellationToken);

		return new PagedResult<BankTransaction>(items, totalCount, query.PageNumber, query.PageSize);
	}
}
