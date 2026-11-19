namespace ZPI4M.Grouping.Services.Access;

/// <summary>Trusted background use case; requires an active coordinator transaction.</summary>
public interface IGroupBudgetContextService
{
	Task<GroupBudgetContext?> LockAsync(Guid groupId, CancellationToken cancellationToken = default);
}
