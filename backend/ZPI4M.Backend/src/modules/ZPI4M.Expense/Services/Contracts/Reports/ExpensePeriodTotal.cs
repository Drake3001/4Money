namespace ZPI4M.Expense.Services.Contracts.Reports;

public sealed record ExpensePeriodTotal(DateOnly PeriodStart, Guid? CategoryId, string? CategoryName, ExpenseReportTotals Totals);
