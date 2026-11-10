namespace ZPI4M.Expense.Services.Contracts;

public sealed record ExpenseShareDetails(
	Guid UserId,
	decimal Percentage,
	decimal Amount);
