using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.DTOs.PasswordReset;

public sealed class ResetPasswordRequest
{
	[Required, StringLength(43, MinimumLength = 43)]
	[RegularExpression(@"^[A-Za-z0-9_-]{43}$")]
	public string Token { get; init; } = string.Empty;

	[Required, StringLength(128, MinimumLength = 8)]
	[RegularExpression(@"^(?=.*\p{Lu})(?=.*[0-9])(?=.*[\p{P}\p{S}]).+$",
		ErrorMessage = "Password must contain at least one uppercase letter, one digit and one special character.")]
	public string NewPassword { get; init; } = string.Empty;
}
