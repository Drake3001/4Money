using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.Services.EmailChange;

public sealed class EmailChangeOptions
{
	public const string SectionName = "EmailChange";

	[Range(1, 168)]
	public int TokenLifetimeHours { get; set; } = 24;

	[Range(1, 3600)]
	public int ResendCooldownSeconds { get; set; } = 60;
}
