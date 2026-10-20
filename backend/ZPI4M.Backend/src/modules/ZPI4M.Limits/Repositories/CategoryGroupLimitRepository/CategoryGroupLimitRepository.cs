using Microsoft.EntityFrameworkCore;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Repositories.CategoryGroupLimitRepository;

public class CategoryGroupLimitRepository : ICategoryGroupLimitRepository
{
	private readonly LimitsDbContext _context;

	public CategoryGroupLimitRepository(LimitsDbContext context)
	{
		_context = context;
	}

	public void Add(CategoryGroupLimit entity)
	{
		_context.CategoryGroupLimits.Add(entity);
	}

	public void Remove(CategoryGroupLimit entity)
	{
		_context.CategoryGroupLimits.Remove(entity);
	}

	public Task<CategoryGroupLimit?> GetAsync(Guid categoryId, Guid groupId, CancellationToken cancellationToken = default)
	{
		return _context.CategoryGroupLimits.SingleOrDefaultAsync(l => l.FkCategoryId == categoryId && l.FkGroupId == groupId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid categoryId, Guid groupId, CancellationToken cancellationToken = default)
	{
		return _context.CategoryGroupLimits.AnyAsync(l => l.FkCategoryId == categoryId && l.FkGroupId == groupId, cancellationToken);
	}

	public async Task<IReadOnlyList<CategoryGroupLimit>> GetByGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
	{
		return await _context.CategoryGroupLimits.Where(l => l.FkGroupId == groupId).OrderBy(l => l.FkCategoryId).ToListAsync(cancellationToken);
	}
}
