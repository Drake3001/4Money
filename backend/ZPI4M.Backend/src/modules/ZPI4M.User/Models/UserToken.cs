using System;

namespace ZPI4M.User.Models;

public enum UserTokenPurpose
{
	EmailConfirmation = 1,
	EmailChange = 2,
	PasswordReset = 3,
	RefreshToken = 4
}

public partial class UserToken
{
	public Guid Id { get; set; }

	public Guid FkUserId { get; set; }

	public string TokenHash { get; set; } = null!;

	public UserTokenPurpose Purpose { get; set; }

	public string? TargetEmail { get; set; }

	public DateTime CreatedAt { get; set; }

	public DateTime ExpiresAt { get; set; }

	public virtual User FkUser { get; set; } = null!;
}
