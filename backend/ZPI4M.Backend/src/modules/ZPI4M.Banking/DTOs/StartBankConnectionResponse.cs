namespace ZPI4M.Banking.DTOs;

public sealed record StartBankConnectionResponse(
	Guid AttemptId,
	string AuthorizationUrl,
	DateTimeOffset ExpiresAt);
