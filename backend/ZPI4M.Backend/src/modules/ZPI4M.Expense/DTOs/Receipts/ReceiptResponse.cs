using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Receipts;

public sealed record ReceiptResponse(
	[property: JsonRequired] Guid Id,
	[property: JsonRequired] Guid ExpenseId,
	[property: JsonRequired] string FileUrl,
	[property: JsonRequired] DateTime FileUrlExpiresAt,
	[property: JsonRequired] ReceiptAnalysisResponse? Analysis);
