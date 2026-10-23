using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Repositories.CategoryGroupLimitRepository;

public interface ICategoryGroupLimitRepository
{
	void Add(CategoryGroupLimit entity);
	void Remove(CategoryGroupLimit entity);
	Task<CategoryGroupLimit?> GetAsync(Guid categoryId, Guid groupId, CancellationToken cancellationToken = default);
	Task<bool> ExistsAsync(Guid categoryId, Guid groupId, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<CategoryGroupLimit>> GetByGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
}
