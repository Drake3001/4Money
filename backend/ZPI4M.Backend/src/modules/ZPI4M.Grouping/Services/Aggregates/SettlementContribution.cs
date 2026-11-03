namespace ZPI4M.Grouping.Services.Aggregates;

public sealed record SettlementContribution(Guid GroupId, Guid FromUserId, Guid ToUserId, decimal Amount);
