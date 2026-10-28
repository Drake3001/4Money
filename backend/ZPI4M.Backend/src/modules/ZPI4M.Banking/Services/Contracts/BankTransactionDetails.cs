namespace ZPI4M.Banking.Services.Contracts;

/// <summary>A local bank transaction, not an expense. Amount and status retain their stored meaning.</summary>
public sealed record BankTransactionDetails(
	Guid Id,
	Guid BankAccountId,
	decimal Amount,
	string CurrencyCode,
	DateOnly Date,
	string? Description,
	string Status);
