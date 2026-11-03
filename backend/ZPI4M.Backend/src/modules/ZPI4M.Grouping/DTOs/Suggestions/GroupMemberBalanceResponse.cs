namespace ZPI4M.Grouping.DTOs.Suggestions;

public sealed record GroupMemberBalanceResponse(Guid UserId, bool IsMember, decimal Balance);
