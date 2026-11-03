using Microsoft.EntityFrameworkCore;
using ZPI4M.Expense.Database;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseRepository;

public class ExpenseRepository : IExpenseRepository
{
	private readonly ExpenseDbContext _context;

	public ExpenseRepository(ExpenseDbContext context)
	{
		_context = context;
	}

	public void Add(Models.Expense entity)
	{
		_context.Expenses.Add(entity);
	}

	public void Remove(Models.Expense entity)
	{
		_context.Expenses.Remove(entity);
	}

	public Task<Models.Expense?> GetByIdAsync(Guid expenseId, CancellationToken cancellationToken = default)
	{
		return _context.Expenses.SingleOrDefaultAsync(e => e.Id == expenseId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid expenseId, CancellationToken cancellationToken = default)
	{
		return _context.Expenses.AnyAsync(e => e.Id == expenseId, cancellationToken);
	}

	public async Task<PagedResult<Models.Expense>> GetByGroupAsync(Guid groupId, ExpenseQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (query.DateFrom > query.DateTo)
			throw new ArgumentException("DateFrom must be earlier than or equal to DateTo.", nameof(query));
		if (query.MinAmount > query.MaxAmount)
			throw new ArgumentException("MinAmount must be less than or equal to MaxAmount.", nameof(query));

		IQueryable<Models.Expense> itemsQuery = _context.Expenses.Where(e => e.FkGroupId == groupId);
		if (!string.IsNullOrWhiteSpace(query.Name))
		{
			var description = query.Name.Trim().ToLowerInvariant();
			itemsQuery = itemsQuery.Where(e => e.Name.ToLower().Contains(description));
		}
		if (!string.IsNullOrWhiteSpace(query.Merchant))
		{
			var merchant = query.Merchant.Trim().ToLowerInvariant();
			itemsQuery = itemsQuery.Where(e => e.Merchant != null && e.Merchant.ToLower().Contains(merchant));
		}
		if (query.DateFrom.HasValue)
		{
			var dateFrom = query.DateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
			itemsQuery = itemsQuery.Where(e => e.TransactionTime >= dateFrom);
		}
		if (query.DateTo.HasValue)
		{
			var dateTo = query.DateTo.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
			itemsQuery = itemsQuery.Where(e => e.TransactionTime.Date <= dateTo);
		}
		if (query.MinAmount.HasValue)
			itemsQuery = itemsQuery.Where(e => e.Amount >= query.MinAmount.Value);
		if (query.MaxAmount.HasValue)
			itemsQuery = itemsQuery.Where(e => e.Amount <= query.MaxAmount.Value);

		IOrderedQueryable<Models.Expense> ordered;
		switch (query.SortBy)
		{
			case ExpenseSortBy.TransactionTime:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.TransactionTime);
				else
					ordered = itemsQuery.OrderBy(e => e.TransactionTime);
				break;
			case ExpenseSortBy.Name:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.Name);
				else
					ordered = itemsQuery.OrderBy(e => e.Name);
				break;
			case ExpenseSortBy.Amount:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.Amount);
				else
					ordered = itemsQuery.OrderBy(e => e.Amount);
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

		return new PagedResult<Models.Expense>(items, totalCount, query.PageNumber, query.PageSize);
	}
}
