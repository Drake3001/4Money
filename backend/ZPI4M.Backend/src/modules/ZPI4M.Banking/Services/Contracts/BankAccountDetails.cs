namespace ZPI4M.Banking.Services.Contracts;

/// <summary>A local account snapshot, including historical and closed accounts. Provider identity stays private.</summary>
public sealed record BankAccountDetails(
	Guid Id,
	string Name,
	string? Number,
	string? Iban,
	string? FinancialInstitutionId,
	string? Type,
	string CurrencyCode,
	bool IsClosed,
	DateTimeOffset? LastSuccessfulSyncAt,
	DateTimeOffset? TransactionsSyncedThrough);
