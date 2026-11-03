namespace ZPI4M.Grouping.DTOs.Suggestions;

public sealed record SuggestedSettlementResponse(Guid FromUserId, Guid ToUserId, decimal Amount);
