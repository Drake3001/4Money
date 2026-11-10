namespace ZPI4M.Expense.Services.Contracts;

public sealed record ExpenseHistoryEntry(Models.Expense Expense, string GroupName, string PayerName,
	ExpenseShareDetails? OwnShare, IReadOnlyList<CategoryDetails> Categories);
