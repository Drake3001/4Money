namespace ZPI4M.Banking.Services.Contracts;

/// <param name="FailureCode">A safe 4Money code, never a raw provider error.</param>
public sealed record BankConnectionAttemptDetails(
	Guid Id,
	BankConnectionAttemptStatus Status,
	Guid? ConnectionId,
	DateTimeOffset CreatedAt,
	DateTimeOffset ExpiresAt,
	DateTimeOffset? CompletedAt,
	string? FailureCode);
