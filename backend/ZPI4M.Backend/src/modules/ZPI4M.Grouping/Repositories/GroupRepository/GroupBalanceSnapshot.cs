namespace ZPI4M.Grouping.Repositories.GroupRepository;

public sealed record GroupBalanceSnapshot(Guid GroupId, string CurrencyCode, IReadOnlyList<GroupBalanceMember> Members);
