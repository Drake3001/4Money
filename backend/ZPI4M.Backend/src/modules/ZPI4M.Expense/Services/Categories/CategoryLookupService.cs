using ZPI4M.Expense.Repositories.CategoryRepository;

namespace ZPI4M.Expense.Services.Categories;

public sealed class CategoryLookupService(ICategoryRepository categories) : ICategoryLookupService
{
	public Task<bool> ExistsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
		categories.ExistsAsync(categoryId, cancellationToken);
	public async Task<string?> GetNameAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
		(await categories.GetByIdAsync(categoryId, cancellationToken))?.Name;
}
