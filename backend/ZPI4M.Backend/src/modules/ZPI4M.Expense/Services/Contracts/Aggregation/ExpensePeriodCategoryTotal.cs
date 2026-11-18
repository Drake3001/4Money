namespace ZPI4M.Expense.Services.Contracts.Aggregation;

/// <summary>
/// Period is the first calendar date of the month, quarter or year.
/// It may precede dateFrom for a partial first period; TotalAmount includes only selected dates.
/// </summary>
public sealed record ExpensePeriodCategoryTotal(DateOnly Period, Guid CategoryId, decimal TotalAmount);
