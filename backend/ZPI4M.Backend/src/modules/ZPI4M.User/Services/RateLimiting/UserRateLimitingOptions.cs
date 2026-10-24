using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.Services.RateLimiting;

public sealed class UserRateLimitingOptions
{
	public const string SectionName = "UserRateLimiting";

	[Range(1, int.MaxValue)]
	public int LoginPermitLimit { get; set; } = 30;

	[Range(1, int.MaxValue)]
	public int RefreshPermitLimit { get; set; } = 120;

	[Range(1, int.MaxValue)]
	public int PasswordResetPermitLimit { get; set; } = 10;
}
