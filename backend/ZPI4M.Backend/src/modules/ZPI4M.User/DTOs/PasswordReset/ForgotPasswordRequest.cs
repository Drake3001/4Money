using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.DTOs.PasswordReset;

public sealed class ForgotPasswordRequest
{
	private string _email = string.Empty;

	[Required, EmailAddress, StringLength(254)]
	public string Email
	{
		get => _email;
		init => _email = value?.Trim().ToLowerInvariant() ?? string.Empty;
	}
}
