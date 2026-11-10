namespace ZPI4M.Expense.Services.Contracts;

public sealed record ExpensePreview(
	PreparedExpense Expense,
	ExpenseLabels Labels,
	Guid? BankTransactionId,
	Guid? ReceiptId);
