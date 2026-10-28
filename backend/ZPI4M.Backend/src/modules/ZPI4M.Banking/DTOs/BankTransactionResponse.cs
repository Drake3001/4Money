namespace ZPI4M.Banking.DTOs;

/// <param name="Amount">Exact signed decimal text with two fractional digits and an invariant decimal point.</param>
public sealed record BankTransactionResponse(
	Guid Id,
	Guid BankAccountId,
	string Amount,
	string CurrencyCode,
	DateOnly Date,
	string? Description,
	string Status);
