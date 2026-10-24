using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.Services.AccountDeactivation;

public sealed class AccountDeactivationOptions
{
	public const string SectionName = "AccountDeactivation";

	[Range(1, 1440)]
	public int TokenLifetimeMinutes { get; set; } = 30;

	[Range(1, 3600)]
	public int ResendCooldownSeconds { get; set; } = 60;
}
