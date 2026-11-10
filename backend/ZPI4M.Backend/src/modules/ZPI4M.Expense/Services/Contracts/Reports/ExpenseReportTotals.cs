namespace ZPI4M.Expense.Services.Contracts.Reports;

public sealed record ExpenseReportTotals(int ExpenseCount, decimal Amount, decimal UserShareAmount, decimal UserPaidAmount);
