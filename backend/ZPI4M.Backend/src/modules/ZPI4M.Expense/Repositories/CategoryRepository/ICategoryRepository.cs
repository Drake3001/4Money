using ZPI4M.Expense.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.CategoryRepository;

public interface ICategoryRepository
{
	Task<Category?> GetByIdAsync(Guid categoryId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid categoryId, CancellationToken cancellationToken = default);
	Task<PagedResult<Category>> GetPageAsync(CategoryQuery query, CancellationToken cancellationToken = default);
}
