using System.ComponentModel;

namespace ZPI4M.Reports.DTOs.PeriodCategoryTotals;

/// <summary>A null CategoryId identifies the combined "Pozostałe" series.</summary>
public sealed record ExpensePeriodCategorySeries(
	[property: Description("Category UUID; null exclusively identifies the combined Other series.")] Guid? CategoryId,
	string Name,
	[property: Description("Chronological calendar periods, including zero totals. Every series has the same periods.")] IReadOnlyList<ExpensePeriodCategoryItem> Items);
