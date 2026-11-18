namespace ZPI4M.Expense.Services.Contracts.Aggregation;

/// <summary>
/// Total selected expense item amounts for a weekday across the requested date range.
/// DayOfWeek uses System.DayOfWeek: Sunday = 0, Monday = 1, through Saturday = 6.
/// </summary>
public sealed record ExpenseDayOfWeekTotal(DayOfWeek DayOfWeek, decimal TotalAmount);
