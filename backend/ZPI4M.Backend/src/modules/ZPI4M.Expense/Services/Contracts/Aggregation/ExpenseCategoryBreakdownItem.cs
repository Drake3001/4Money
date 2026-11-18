namespace ZPI4M.Expense.Services.Contracts.Aggregation;

public sealed record ExpenseCategoryBreakdownItem(Guid CategoryId, decimal TotalAmount);
