using System.Text.Json.Serialization;
using ZPI4M.Expense.DTOs.Categories;

namespace ZPI4M.Expense.DTOs.Expenses;

public sealed record ExpenseListItemResponse(
	[property: JsonRequired] Guid Id,
	[property: JsonRequired] Guid GroupId,
	[property: JsonRequired] string GroupName,
	[property: JsonRequired] Guid PayerId,
	[property: JsonRequired] string PayerDisplayName,
	[property: JsonRequired] string Name,
	[property: JsonRequired] string? Merchant,
	[property: JsonRequired] string Amount,
	[property: JsonRequired] string CurrencyCode,
	[property: JsonRequired] DateOnly TransactionTime,
	[property: JsonRequired] Guid? BankTransactionId,
	[property: JsonRequired] Guid? ReceiptId,
	[property: JsonRequired] string MyShareAmount,
	[property: JsonRequired] string MySharePercentage,
	[property: JsonRequired] IReadOnlyList<CategoryResponse> Categories,
	[property: JsonRequired] bool HasReceipt,
	[property: JsonRequired] bool HasBankTransaction);
