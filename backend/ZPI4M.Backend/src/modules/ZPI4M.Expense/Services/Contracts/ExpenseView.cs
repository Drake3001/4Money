namespace ZPI4M.Expense.Services.Contracts;

public sealed record ExpenseView(ExpenseDetails Expense, ExpenseLabels Labels, bool CanEdit, bool CanDelete);
