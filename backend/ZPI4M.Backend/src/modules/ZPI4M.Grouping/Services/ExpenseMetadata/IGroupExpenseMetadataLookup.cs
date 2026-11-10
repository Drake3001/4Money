namespace ZPI4M.Grouping.Services.ExpenseMetadata;

/// <summary>Internal metadata only; callers must enforce Expense read/write authorization.</summary>
public interface IGroupExpenseMetadataLookup
{
	Task<GroupExpenseMetadataResult> GetAsync(Guid groupId, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<Guid>> GetActiveGroupIdsAsync(Guid actor, CancellationToken cancellationToken = default);
}

public sealed record GroupExpenseMetadata(Guid Id, string Name, string CurrencyCode, bool IsArchived);
public sealed record GroupExpenseMetadataResult(int Status, GroupExpenseMetadata? Value = null);
