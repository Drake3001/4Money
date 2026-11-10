using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Receipts;

public sealed record ReceiptDraft(
	[property: JsonRequired] string? Name,
	[property: JsonRequired] string? Merchant,
	[property: JsonRequired] string? Amount,
	[property: JsonRequired] string? CurrencyCode,
	[property: JsonRequired] DateOnly? TransactionTime,
	[property: JsonRequired] IReadOnlyList<ReceiptDraftItem> Items);
