using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Receipts;

public sealed record ReceiptDraftItem(
	[property: JsonRequired] Guid? CategoryId,
	[property: JsonRequired] string? Name,
	[property: JsonRequired] string? UnitPrice,
	[property: JsonRequired] string? Quantity,
	[property: JsonRequired] string? Discount,
	[property: JsonRequired] string? Unit,
	[property: JsonRequired] bool NeedsReview,
	[property: JsonRequired] string? ReviewReason);
