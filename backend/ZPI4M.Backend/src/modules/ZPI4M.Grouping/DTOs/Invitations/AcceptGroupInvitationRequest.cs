using System.ComponentModel.DataAnnotations;

namespace ZPI4M.Grouping.DTOs.Invitations;

public sealed class AcceptGroupInvitationRequest
{
	[Required]
	public string? Token { get; init; }
}
