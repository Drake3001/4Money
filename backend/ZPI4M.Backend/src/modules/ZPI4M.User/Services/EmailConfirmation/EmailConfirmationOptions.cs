using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.Services.EmailConfirmation;

public sealed class EmailConfirmationOptions
{
	public const string SectionName = "EmailConfirmation";

	[Range(1, 168)]
	public int TokenLifetimeHours { get; set; } = 24;

	[Range(1, 3600)]
	public int ResendCooldownSeconds { get; set; } = 60;
}
