namespace ZPI4M.Grouping.DTOs.Suggestions;

public sealed record GroupSettlementSuggestionsResponse(Guid GroupId, string CurrencyCode, DateTimeOffset GeneratedAt, string Algorithm, bool OptimalityGuaranteed, IReadOnlyList<GroupMemberBalanceResponse> Balances, IReadOnlyList<SuggestedSettlementResponse> Suggestions, decimal TotalAmount);
