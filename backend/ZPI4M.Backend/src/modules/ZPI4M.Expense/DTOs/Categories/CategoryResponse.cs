using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Categories;

public sealed record CategoryResponse(
	[property: JsonRequired] Guid Id,
	[property: JsonRequired] string Name,
	[property: JsonRequired] string Description,
	[property: JsonRequired] string Icon,
	[property: JsonRequired] bool IsSystemGenerated);
