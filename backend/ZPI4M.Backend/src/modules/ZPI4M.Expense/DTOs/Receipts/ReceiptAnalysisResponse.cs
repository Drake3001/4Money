using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Receipts;

public sealed record ReceiptAnalysisResponse(
	[property: JsonRequired] ReceiptDraft Proposal,
	[property: JsonRequired] IReadOnlyList<ReceiptWarning> Warnings,
	[property: JsonRequired] bool NeedsReview,
	[property: JsonRequired] JsonElement RawResult);
