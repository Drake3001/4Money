namespace ZPI4M.Expense.Services.Contracts.Aggregation;

public sealed record ExpenseDailyTotalsItem(DateOnly Date, decimal TotalAmount);
