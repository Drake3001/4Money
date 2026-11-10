namespace ZPI4M.Expense.Repositories.ExpenseRepository;

/// <summary>Expense and its shares read in one SQL statement.</summary>
public sealed record ExpenseReadRow(Models.Expense Expense, IReadOnlyList<Models.ExpenseShare> Shares);
