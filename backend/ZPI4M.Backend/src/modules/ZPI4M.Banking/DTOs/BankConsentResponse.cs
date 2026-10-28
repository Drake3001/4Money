namespace ZPI4M.Banking.DTOs;

public sealed record BankConsentResponse(
	Guid ConnectionId,
	string ProviderStatus,
	DateTimeOffset? SessionExpiresAt,
	bool? SessionExtendable);
