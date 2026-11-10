using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Expenses;

public sealed record ExpenseShareResponse(
	[property: JsonRequired] Guid UserId,
	[property: JsonRequired] string UserDisplayName,
	[property: JsonRequired] string Percentage,
	[property: JsonRequired] string Amount);
