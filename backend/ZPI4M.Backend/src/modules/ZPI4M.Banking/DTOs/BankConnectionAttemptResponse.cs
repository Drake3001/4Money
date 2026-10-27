using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.DTOs;

public sealed record BankConnectionAttemptResponse(
	Guid Id,
	BankConnectionAttemptStatus Status,
	Guid? ConnectionId,
	DateTimeOffset CreatedAt,
	DateTimeOffset ExpiresAt,
	DateTimeOffset? CompletedAt,
	string? FailureCode);
