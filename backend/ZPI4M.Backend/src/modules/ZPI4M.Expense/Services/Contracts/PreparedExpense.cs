namespace ZPI4M.Expense.Services.Contracts;

public sealed record PreparedExpense(
	Guid GroupId,
	Guid PayerId,
	string Name,
	string? Merchant,
	decimal Amount,
	DateTime TransactionTime,
	IReadOnlyList<PreparedExpenseItem> Items,
	IReadOnlyList<ExpenseShareDetails> Shares);
