namespace ZPI4M.Grouping.DTOs.Invitations;

public sealed record CreateGroupInvitationResponse(
	GroupInvitationResponse Invitation,
	string Token);
