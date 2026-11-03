namespace ZPI4M.Grouping.Repositories.GroupRepository;

public sealed record GroupBalanceMember(Guid UserId, bool IsMember, decimal TotalPaid, decimal TotalShare, decimal TotalSettlementsSent, decimal TotalSettlementsReceived);
