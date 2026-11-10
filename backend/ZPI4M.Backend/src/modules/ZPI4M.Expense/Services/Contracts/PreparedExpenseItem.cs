namespace ZPI4M.Expense.Services.Contracts;

public sealed record PreparedExpenseItem(
	Guid CategoryId,
	string Name,
	decimal UnitPrice,
	decimal Quantity,
	decimal Discount,
	string? Unit,
	decimal Total,
	bool IsSystemGenerated)
{
	public Guid? Id { get; init; }
}
