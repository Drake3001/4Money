namespace ZPI4M.Expense.Services.Contracts.Reports;

public sealed record ExpenseCategoryTotal(Guid CategoryId, string CategoryName, ExpenseReportTotals Totals);
