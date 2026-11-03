namespace ZPI4M.Grouping.DTOs.Members;

public sealed record GroupMemberResponse(
	Guid UserId,
	Guid GroupId,
	bool IsMember,
	DateTime JoinedAt,
	decimal TotalPaid,
	decimal TotalShare,
	decimal TotalSettlementsSent,
	decimal TotalSettlementsReceived);
