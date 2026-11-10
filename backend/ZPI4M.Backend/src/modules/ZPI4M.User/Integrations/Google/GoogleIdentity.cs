namespace ZPI4M.User.Integrations.Google;

public sealed record GoogleIdentity(
	string Subject,
	string Email,
	bool EmailVerified,
	string? GivenName,
	string? FamilyName,
	string? HostedDomain);
