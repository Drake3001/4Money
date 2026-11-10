using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Expenses;

public sealed record ExpensePreviewResponse(
	[property: JsonRequired] string Amount,
	[property: JsonRequired] string CurrencyCode,
	[property: JsonRequired] IReadOnlyList<PreviewItemResponse> Items,
	[property: JsonRequired] IReadOnlyList<ExpenseShareResponse> Shares,
	[property: JsonRequired] Guid? BankTransactionId,
	[property: JsonRequired] Guid? ReceiptId);
