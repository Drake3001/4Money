using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.Services.PasswordReset;

public sealed class PasswordResetOptions
{
	public const string SectionName = "PasswordReset";

	[Range(1, 1440)]
	public int TokenLifetimeMinutes { get; set; } = 30;

	[Range(1, 3600)]
	public int ResendCooldownSeconds { get; set; } = 60;
}
