using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.DTOs.Authentication;

public sealed class GoogleLoginRequest
{
	[Required, StringLength(16384)]
	public string IdToken { get; init; } = string.Empty;
}
