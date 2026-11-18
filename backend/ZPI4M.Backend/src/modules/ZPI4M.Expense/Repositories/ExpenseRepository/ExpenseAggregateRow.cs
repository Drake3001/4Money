namespace ZPI4M.Expense.Repositories.ExpenseRepository;

public sealed record ExpenseAggregateRow(DateOnly Period, Guid CategoryId, int Weekday, decimal TotalAmount);
