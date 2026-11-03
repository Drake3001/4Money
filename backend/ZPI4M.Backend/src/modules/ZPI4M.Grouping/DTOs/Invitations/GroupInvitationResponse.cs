namespace ZPI4M.Grouping.DTOs.Invitations;

public sealed record GroupInvitationResponse(
	Guid Id,
	Guid GroupId,
	Guid CreatedByUserId,
	int MaxUses,
	int UseCount,
	DateTimeOffset ExpiresAt,
	bool IsActive);
