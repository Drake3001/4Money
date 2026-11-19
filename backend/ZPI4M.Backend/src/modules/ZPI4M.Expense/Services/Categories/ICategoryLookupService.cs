namespace ZPI4M.Expense.Services.Categories;

public interface ICategoryLookupService
{
	Task<bool> ExistsAsync(Guid categoryId, CancellationToken cancellationToken = default);
	Task<string?> GetNameAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
