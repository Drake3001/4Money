using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.CategoryRepository;

public enum CategorySortBy { Name }

public class CategoryQuery : PagedQuery
{
	public string? Name { get; set; }
	public CategorySortBy SortBy { get; set; } = CategorySortBy.Name;
}
