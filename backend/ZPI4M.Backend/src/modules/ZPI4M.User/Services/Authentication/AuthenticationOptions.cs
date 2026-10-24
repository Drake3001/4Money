using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.Services.Authentication;

public sealed class AuthenticationOptions
{
	public const string SectionName = "UserAuthentication";

	[Required]
	public string Issuer { get; set; } = "4Money";

	[Required]
	public string Audience { get; set; } = "4Money.Client";

	[Required, MinLength(32)]
	public string SigningKey { get; set; } = string.Empty;

	[Range(1, 60)]
	public int AccessTokenLifetimeMinutes { get; set; } = 15;

	[Range(1, 90)]
	public int RefreshTokenLifetimeDays { get; set; } = 7;
}
