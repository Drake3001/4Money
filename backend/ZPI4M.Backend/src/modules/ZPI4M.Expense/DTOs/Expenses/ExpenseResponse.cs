using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Expenses;

public sealed record ExpenseResponse(
	[property: JsonRequired] Guid Id,
	[property: JsonRequired] Guid GroupId,
	[property: JsonRequired] string GroupName,
	[property: JsonRequired] Guid PayerId,
	[property: JsonRequired] string PayerDisplayName,
	[property: JsonRequired] string Name,
	[property: JsonRequired] string? Merchant,
	[property: JsonRequired] string Amount,
	[property: JsonRequired] string CurrencyCode,
	[property: JsonRequired] DateOnly TransactionTime,
	[property: JsonRequired] Guid? BankTransactionId,
	[property: JsonRequired] Guid? ReceiptId,
	[property: JsonRequired] IReadOnlyList<ExpenseItemResponse> Items,
	[property: JsonRequired] IReadOnlyList<ExpenseShareResponse> Shares,
	[property: JsonRequired] string Version,
	[property: JsonRequired] bool CanEdit,
	[property: JsonRequired] bool CanDelete);
