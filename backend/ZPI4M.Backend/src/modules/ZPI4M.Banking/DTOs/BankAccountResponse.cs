namespace ZPI4M.Banking.DTOs;

public sealed record BankAccountResponse(
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
