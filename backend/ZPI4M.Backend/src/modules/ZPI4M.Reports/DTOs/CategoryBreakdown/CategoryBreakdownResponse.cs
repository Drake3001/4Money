namespace ZPI4M.Reports.DTOs.CategoryBreakdown;

public sealed record CategoryBreakdownResponse(
	IReadOnlyList<CategoryBreakdownItemResponse> Items);
