using Microsoft.EntityFrameworkCore;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseItemRepository;

public class ExpenseItemRepository : IExpenseItemRepository
{
	private readonly ExpenseDbContext _context;

	public ExpenseItemRepository(ExpenseDbContext context)
	{
		_context = context;
	}

	public void Add(ExpenseItem entity)
	{
		_context.ExpenseItems.Add(entity);
	}

	public void AddRange(IEnumerable<ExpenseItem> items)
	{
		_context.ExpenseItems.AddRange(items);
	}

	public void Remove(ExpenseItem entity)
	{
		_context.ExpenseItems.Remove(entity);
	}

	public Task<ExpenseItem?> GetByIdAsync(Guid expenseItemId, CancellationToken cancellationToken = default)
	{
		return _context.ExpenseItems.SingleOrDefaultAsync(i => i.Id == expenseItemId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid expenseItemId, CancellationToken cancellationToken = default)
	{
		return _context.ExpenseItems.AnyAsync(i => i.Id == expenseItemId, cancellationToken);
	}

	public async Task<PagedResult<ExpenseItem>> GetByExpenseAsync(Guid expenseId, ExpenseItemQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		IQueryable<ExpenseItem> itemsQuery = _context.ExpenseItems.Where(i => i.FkExpenseId == expenseId);
		if (!string.IsNullOrWhiteSpace(query.Name))
		{
			var name = query.Name.Trim().ToLowerInvariant();
			itemsQuery = itemsQuery.Where(i => i.Name.ToLower().Contains(name));
		}
		if (query.CategoryId.HasValue)
			itemsQuery = itemsQuery.Where(i => i.FkCategoryId == query.CategoryId.Value);
		if (query.UserId.HasValue)
			itemsQuery = itemsQuery.Where(i => i.FkUserId == query.UserId.Value);

		IOrderedQueryable<ExpenseItem> ordered;
		switch (query.SortBy)
		{
			case ExpenseItemSortBy.Name:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(i => i.Name);
				else
					ordered = itemsQuery.OrderBy(i => i.Name);
				break;
			case ExpenseItemSortBy.UnitPrice:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(i => i.UnitPrice);
				else
					ordered = itemsQuery.OrderBy(i => i.UnitPrice);
				break;
			case ExpenseItemSortBy.Quantity:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(i => i.Quantity);
				else
					ordered = itemsQuery.OrderBy(i => i.Quantity);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(query), "Unknown sort field.");
		}

		int offset = query.GetOffset();
		int totalCount = await itemsQuery.CountAsync(cancellationToken);
		var items = await ordered.ThenBy(i => i.Id)
			.Skip(offset)
			.Take(query.PageSize)
			.ToListAsync(cancellationToken);

		return new PagedResult<ExpenseItem>(items, totalCount, query.PageNumber, query.PageSize);
	}
}
