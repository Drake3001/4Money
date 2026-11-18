using System.ComponentModel;

namespace ZPI4M.Reports.DTOs.CategoryBreakdown;

/// <summary>A null CategoryId identifies the combined "Pozostałe" item.</summary>
public sealed record CategoryBreakdownItemResponse(
	[property: Description("Category UUID; null exclusively identifies the combined Other item, regardless of its display name.")] Guid? CategoryId,
	string Name,
	[property: Description("Total selected expense amount. Signed decimal; no presentation rounding.")] decimal TotalAmount);
