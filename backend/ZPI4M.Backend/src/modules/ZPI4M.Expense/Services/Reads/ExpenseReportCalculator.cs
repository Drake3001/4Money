using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Contracts.Reports;
using ZPI4M.Expense.Services.Expenses;

namespace ZPI4M.Expense.Services.Reads;

/// <summary>Uses the same rounded line values and user shares as Expense.</summary>
public sealed class ExpenseReportCalculator(IExpenseCalculationService calculations)
{
	public ExpenseReportEntry CreateEntry(ExpenseReadRow row, Guid userId)
	{
		var expense = row.Expense;
		var share = calculations.CalculateShares(expense.Amount, row.Shares.Select(value => (value.FkUserId, value.Percentage)))
			.FirstOrDefault(value => value.UserId == userId);
		var categories = expense.ExpenseItems.GroupBy(item => new { item.FkCategoryId, item.FkCategory.Name })
			.OrderBy(group => group.Key.FkCategoryId.ToString("D"), StringComparer.Ordinal)
			.Select(group => new
			{
				Id = group.Key.FkCategoryId,
				group.Key.Name,
				Amount = group.Sum(item => ExpenseCalculationService.CalculateItemTotal(item.UnitPrice, item.Quantity, item.Discount))
			}).ToArray();
		// Floor also handles negative category amounts. Distribute remaining cents so categories equal the actual user share.
		var exact = categories.Select(category => category.Amount * (share?.Percentage ?? 0m)).ToArray();
		var cents = exact.Select(decimal.Floor).ToArray();
		var remaining = decimal.ToInt32((share?.Amount ?? 0m) * 100m - cents.Sum());
		foreach (var index in Enumerable.Range(0, categories.Length)
			.OrderByDescending(index => exact[index] - cents[index]).ThenBy(index => index).Take(remaining))
			cents[index]++;
		var categoryTotals = categories.Select((category, index) => new ExpenseCategoryTotal(category.Id, category.Name,
			new(1, category.Amount, cents[index] / 100m, expense.FkPayerId == userId ? category.Amount : 0m))).ToArray();
		return new(expense.Id, expense.FkGroupId, expense.FkPayerId, expense.Name, expense.Merchant,
			DateOnly.FromDateTime(expense.TransactionTime), expense.Amount, share?.Amount ?? 0m,
			expense.FkReceiptId.HasValue, expense.FkBankTransactionId.HasValue, categoryTotals);
	}

	public static ExpenseReportTotals Sum(IEnumerable<ExpenseReportTotals> values)
	{
		var rows = values.ToArray();
		return new(rows.Sum(row => row.ExpenseCount), rows.Sum(row => row.Amount),
			rows.Sum(row => row.UserShareAmount), rows.Sum(row => row.UserPaidAmount));
	}

	public static ExpenseReportTotals Total(IEnumerable<ExpenseReportEntry> entries, Guid userId, Guid? categoryId) =>
		Sum(entries.Select(entry => categoryId.HasValue
			? entry.Categories.Single(category => category.CategoryId == categoryId).Totals
			: new ExpenseReportTotals(1, entry.Amount, entry.UserShareAmount, entry.PayerId == userId ? entry.Amount : 0m)));
}
