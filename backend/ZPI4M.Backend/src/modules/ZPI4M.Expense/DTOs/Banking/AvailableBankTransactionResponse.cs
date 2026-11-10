using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Banking;

public sealed record AvailableBankTransactionResponse(
	[property: JsonRequired] Guid Id,
	[property: JsonRequired] Guid BankAccountId,
	[property: JsonRequired] string Amount,
	[property: JsonRequired] string CurrencyCode,
	[property: JsonRequired] DateOnly Date,
	[property: JsonRequired] string? Description,
	[property: JsonRequired] string Status);
