namespace ZPI4M.Expense.Services.Contracts;

public sealed record ExpenseItemDetails(
	Guid Id,
	Guid CategoryId,
	string Name,
	decimal UnitPrice,
	decimal Quantity,
	decimal Discount,
	string? Unit,
	decimal Total,
	bool IsSystemGenerated);
