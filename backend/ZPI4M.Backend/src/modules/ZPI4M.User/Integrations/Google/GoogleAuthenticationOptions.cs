using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.Integrations.Google;

public sealed class GoogleAuthenticationOptions
{
	public const string SectionName = "GoogleAuthentication";
	public bool Enabled { get; set; }
	public string ClientId { get; set; } = string.Empty;

	[Range(1, 10)]
	public int ChallengeLifetimeMinutes { get; set; } = 5;

	[Range(1, 100000)]
	public int MaxPendingChallenges { get; set; } = 10000;

	[Range(1, 60)]
	public int HttpTimeoutSeconds { get; set; } = 10;
}
