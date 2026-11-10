using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Expenses;

public sealed record ExpenseItemResponse(
	[property: JsonRequired] Guid Id,
	[property: JsonRequired] Guid CategoryId,
	[property: JsonRequired] string CategoryName,
	[property: JsonRequired] string CategoryIcon,
	[property: JsonRequired] string Name,
	[property: JsonRequired] string UnitPrice,
	[property: JsonRequired] string Quantity,
	[property: JsonRequired] string Discount,
	[property: JsonRequired] string? Unit,
	[property: JsonRequired] string Total,
	[property: JsonRequired] bool IsSystemGenerated);
