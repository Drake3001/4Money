using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.DTOs.AccountDeactivation;

public sealed class ConfirmAccountDeactivationRequest
{
	[Required, StringLength(43, MinimumLength = 43)]
	[RegularExpression(@"^[A-Za-z0-9_-]{43}$")]
	public string Token { get; init; } = string.Empty;
}
