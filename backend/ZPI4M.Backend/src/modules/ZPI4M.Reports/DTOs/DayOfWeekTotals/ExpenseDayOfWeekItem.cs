using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ZPI4M.Reports.DTOs.DayOfWeekTotals;

/// <summary>Average includes every occurrence in the inclusive range, including days without expenses.</summary>
public sealed record ExpenseDayOfWeekItem(
	[property: JsonConverter(typeof(JsonStringEnumConverter<DayOfWeek>))]
	[property: Description("Monday through Sunday, returned in that order; names are JSON strings.")] DayOfWeek DayOfWeek,
	[property: Description("Sum divided by all occurrences of this weekday in the range, including days without expenses. Zero if the weekday does not occur.")] decimal AverageAmount);
