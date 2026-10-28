namespace ZPI4M.Banking.Services.Contracts;

/// <summary>The authorization URL is sensitive and must not be logged or cached.</summary>
public sealed record StartBankConnectionResult(
	Guid AttemptId,
	string AuthorizationUrl,
	DateTimeOffset ExpiresAt);
