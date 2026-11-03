using System.ComponentModel.DataAnnotations;

namespace ZPI4M.Grouping.DTOs.Invitations;

public sealed class CreateGroupInvitationRequest
{
	[Range(1, int.MaxValue)]
	public int MaxUses { get; init; }

	[Required]
	public DateTimeOffset? ExpiresAt { get; init; }
}
