namespace ZPI4M.Reports.DTOs.PeriodCategoryTotals;

/// <summary>Period is the first calendar date of the month, quarter or year.</summary>
public sealed record ExpensePeriodCategoryItem(DateOnly Period, decimal TotalAmount);
