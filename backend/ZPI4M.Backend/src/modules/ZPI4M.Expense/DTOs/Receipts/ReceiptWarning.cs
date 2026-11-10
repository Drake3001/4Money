using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Receipts;

public sealed record ReceiptWarning(
	[property: JsonRequired] string? Code,
	[property: JsonRequired] string Message,
	[property: JsonRequired] string? Field);
