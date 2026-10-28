namespace ZPI4M.Banking.Services.Contracts;

/// <param name="LastSyncFailureCode">A safe 4Money code, never a raw provider error.</param>
public sealed record BankConnectionDetails(
	Guid Id,
	string? ProviderName,
	BankConnectionStatus ConnectionStatus,
	BankSynchronizationStatus SyncStatus,
	DateTimeOffset CreatedAt,
	DateTimeOffset? ConnectedAt,
	DateTimeOffset? ConsentExpiresAt,
	DateTimeOffset? LastSuccessfulSyncAt,
	string? LastSyncFailureCode);
