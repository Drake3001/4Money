namespace ZPI4M.Banking.DTOs;

public sealed record StartBankRefreshResponse(
	Guid RefreshId,
	string Status,
	DateTimeOffset CreatedAt);

public sealed record BankRefreshAccountResponse(
	Guid AccountId,
	string Status,
	int AddedTransactions,
	int UpdatedTransactions,
	DateTimeOffset? LastSuccessfulSyncAt,
	string? FailureCode);

public sealed record BankRefreshResponse(
	Guid RefreshId,
	string Status,
	DateTimeOffset CreatedAt,
	DateTimeOffset? StartedAt,
	DateTimeOffset? CompletedAt,
	int TotalAccounts,
	int SuccessfulAccounts,
	int FailedAccounts,
	string? FailureCode,
	IReadOnlyList<BankRefreshAccountResponse> Accounts);
