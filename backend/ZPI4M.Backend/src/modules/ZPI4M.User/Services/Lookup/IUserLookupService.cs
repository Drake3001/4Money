namespace ZPI4M.User.Services.Lookup;

public interface IUserLookupService
{
	Task<IReadOnlyCollection<Guid>> GetActiveUserIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
