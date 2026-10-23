using Microsoft.EntityFrameworkCore;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.CategoryRepository;

public class CategoryRepository : ICategoryRepository
{
	private readonly ExpenseDbContext _context;

	public CategoryRepository(ExpenseDbContext context)
	{
		_context = context;
	}

	public Task<Category?> GetByIdAsync(Guid categoryId, CancellationToken cancellationToken = default)
	{
		return _context.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid categoryId, CancellationToken cancellationToken = default)
	{
		return _context.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken);
	}

	public async Task<PagedResult<Category>> GetPageAsync(CategoryQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		IQueryable<Category> categories = _context.Categories;
		if (!string.IsNullOrWhiteSpace(query.Name))
		{
			var name = query.Name.Trim().ToLowerInvariant();
			categories = categories.Where(c => c.Name.ToLower().Contains(name));
		}

		IOrderedQueryable<Category> ordered;
		switch (query.SortBy)
		{
			case CategorySortBy.Name:
				if (query.Descending)
					ordered = categories.OrderByDescending(c => c.Name);
				else
					ordered = categories.OrderBy(c => c.Name);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(query), "Unknown sort field.");
		}

		int offset = query.GetOffset();
		int totalCount = await categories.CountAsync(cancellationToken);
		var items = await ordered.ThenBy(c => c.Id)
			.Skip(offset)
			.Take(query.PageSize)
			.ToListAsync(cancellationToken);

		return new PagedResult<Category>(items, totalCount, query.PageNumber, query.PageSize);
	}
}
