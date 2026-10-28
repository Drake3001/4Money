using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.DTOs.Authentication;

public sealed class RefreshTokenRequest
{
	[Required, StringLength(43, MinimumLength = 43)]
	[RegularExpression(@"^[A-Za-z0-9_-]{43}$")]
	public string RefreshToken { get; init; } = string.Empty;
}
