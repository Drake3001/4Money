namespace ZPI4M.Expense.Services.Contracts.Aggregation;

public sealed record ExpenseUsageTotal(decimal Amount, string CurrencyCode = "PLN");
