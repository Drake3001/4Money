namespace ZPI4M.Expense.Services.Contracts;

public enum ExpenseErrorCode
{
	NotFound,
	Forbidden,
	Conflict,
	InvalidExpense,
	PayloadTooLarge,
	UnsupportedReceiptFormat,
	UnprocessableReceipt,
	OcrFailure,
	OcrTimeout,
	TemporarilyUnavailable
}
