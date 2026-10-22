using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.DTOs.Authentication;

public sealed class LoginRequest
{
	private string _login = string.Empty;

	[Required, StringLength(100, MinimumLength = 3)]
	public string Login
	{
		get => _login;
		init => _login = value?.Trim().ToLowerInvariant() ?? string.Empty;
	}

	[Required, StringLength(128)]
	public string Password { get; init; } = string.Empty;
}
