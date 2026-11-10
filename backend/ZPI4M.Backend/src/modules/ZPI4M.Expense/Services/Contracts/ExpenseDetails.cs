namespace ZPI4M.Expense.Services.Contracts;

/// <summary>Expense details for service callers; numeric values retain decimal precision.</summary>
public sealed record ExpenseDetails(
	Guid Id,
	Guid GroupId,
	Guid PayerId,
	string Name,
	string? Merchant,
	decimal Amount,
	DateTime TransactionTime,
	Guid? BankTransactionId,
	Guid? ReceiptId,
	IReadOnlyList<ExpenseItemDetails> Items,
	IReadOnlyList<ExpenseShareDetails> Shares)
{
	public Guid Version { get; init; }
}
