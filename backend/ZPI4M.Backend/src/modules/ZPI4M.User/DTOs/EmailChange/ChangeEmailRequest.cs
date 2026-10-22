using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.DTOs.EmailChange;

public sealed class ChangeEmailRequest
{
	private string _newEmail = string.Empty;

	[Required, EmailAddress, StringLength(254)]
	public string NewEmail
	{
		get => _newEmail;
		init => _newEmail = value?.Trim().ToLowerInvariant() ?? string.Empty;
	}

	[Required, StringLength(128)]
	public string CurrentPassword { get; init; } = string.Empty;
}
