namespace ZPI4M.User.Services.Lookup;

/// <summary>Internal labels for already authorized domain objects, including inactive historical accounts.</summary>
public interface IUserDisplayNameLookup
{
	Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
