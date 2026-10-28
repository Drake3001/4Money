using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.DTOs;

public sealed record BankConnectionResponse(
	Guid Id,
	string? ProviderName,
	BankConnectionStatus ConnectionStatus,
	BankSynchronizationStatus SyncStatus,
	DateTimeOffset CreatedAt,
	DateTimeOffset? ConnectedAt,
	DateTimeOffset? ConsentExpiresAt,
	DateTimeOffset? LastSuccessfulSyncAt,
	string? LastSyncFailureCode);
