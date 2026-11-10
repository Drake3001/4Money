namespace ZPI4M.Expense.Services.Contracts.Reports;

public sealed record ExpenseReportEntry(Guid Id, Guid GroupId, Guid PayerId, string Name, string? Merchant,
	DateOnly Date, decimal Amount, decimal UserShareAmount, bool HasReceipt, bool HasBankTransaction,
	IReadOnlyList<ExpenseCategoryTotal> Categories);
