using System.Globalization;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Expense.DTOs.Banking;
using ZPI4M.Expense.DTOs.Categories;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Services.Contracts;

namespace ZPI4M.Expense.ApiControllers;

internal static class ExpenseResponseMapper
{
	internal static AvailableBankTransactionResponse AvailableBankTransaction(BankTransactionDetails transaction) =>
		new(transaction.Id, transaction.BankAccountId, Money(transaction.Amount), transaction.CurrencyCode.ToUpperInvariant(),
			transaction.Date, transaction.Description, transaction.Status);

	internal static CategoryResponse Category(CategoryDetails category) =>
		new(category.Id, category.Name, category.Description, category.Icon, category.IsSystemGenerated);

	internal static ExpenseListItemResponse HistoryEntry(ExpenseHistoryEntry entry)
	{
		var expense = entry.Expense;
		return new(expense.Id, expense.FkGroupId, entry.GroupName, expense.FkPayerId, entry.PayerName,
			expense.Name, expense.Merchant, Money(expense.Amount), "PLN", DateOnly.FromDateTime(expense.TransactionTime),
			expense.FkBankTransactionId, expense.FkReceiptId, Money(entry.OwnShare?.Amount ?? 0),
			Money(entry.OwnShare?.Percentage ?? 0), entry.Categories.Select(Category).ToArray(),
			expense.FkReceiptId.HasValue, expense.FkBankTransactionId.HasValue);
	}

	internal static ExpenseResponse Details(ExpenseView view)
	{
		var expense = view.Expense;
		return new(expense.Id, expense.GroupId, view.Labels.GroupName, expense.PayerId, view.Labels.Users[expense.PayerId],
			expense.Name, expense.Merchant, Money(expense.Amount), "PLN", DateOnly.FromDateTime(expense.TransactionTime), expense.BankTransactionId,
			expense.ReceiptId, expense.Items.Select(item => new ExpenseItemResponse(item.Id, item.CategoryId,
				view.Labels.Categories[item.CategoryId].Name, view.Labels.Categories[item.CategoryId].Icon, item.Name,
				Money(item.UnitPrice), item.Quantity.ToString("F3", CultureInfo.InvariantCulture), Money(item.Discount), item.Unit,
				Money(item.Total), item.IsSystemGenerated)).ToArray(), Shares(expense.Shares, view.Labels),
			expense.Version.ToString("D"), view.CanEdit, view.CanDelete);
	}

	internal static ExpensePreviewResponse Preview(ExpensePreview view) => new(Money(view.Expense.Amount), "PLN",
		view.Expense.Items.Select(item => new PreviewItemResponse(item.CategoryId, view.Labels.Categories[item.CategoryId].Name,
			view.Labels.Categories[item.CategoryId].Icon, item.Name, Money(item.UnitPrice),
			item.Quantity.ToString("F3", CultureInfo.InvariantCulture), Money(item.Discount), item.Unit,
			Money(item.Total), item.IsSystemGenerated)).ToArray(), Shares(view.Expense.Shares, view.Labels), view.BankTransactionId, view.ReceiptId);

	private static IReadOnlyList<ExpenseShareResponse> Shares(IReadOnlyList<ExpenseShareDetails> shares, ExpenseLabels labels) =>
		shares.Select(share => new ExpenseShareResponse(share.UserId, labels.Users[share.UserId],
			Money(share.Percentage), Money(share.Amount))).ToArray();
	internal static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
}
