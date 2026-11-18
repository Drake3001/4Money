namespace ZPI4M.Reports.DTOs.DayOfWeekTotals;

public sealed record ExpenseTotalsByDayOfWeekResponse(IReadOnlyList<ExpenseDayOfWeekItem> Items);
