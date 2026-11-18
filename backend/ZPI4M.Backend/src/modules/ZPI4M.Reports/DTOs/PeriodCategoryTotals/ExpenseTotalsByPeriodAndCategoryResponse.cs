using System.Text.Json.Serialization;
using ZPI4M.Expense.Services.Contracts.Aggregation;

namespace ZPI4M.Reports.DTOs.PeriodCategoryTotals;

public sealed record ExpenseTotalsByPeriodAndCategoryResponse(
	IReadOnlyList<ExpensePeriodCategorySeries> Series,
	[property: JsonConverter(typeof(JsonStringEnumConverter<ExpenseTotalsGranularity>))]
	ExpenseTotalsGranularity Granularity);
