using Xunit;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Expense.Services.Reads;

namespace ZPI4M.Expense.UnitTests;

public sealed class ExpenseReportCalculatorTests
{
	private static readonly Guid Anna = Guid.Parse("10000000-0000-0000-0000-000000000001");
	private static readonly Guid Bartek = Guid.Parse("10000000-0000-0000-0000-000000000002");
	private readonly ExpenseReportCalculator calculator = new(new ExpenseCalculationService());

	[Theory]
	[InlineData(0.01, 0.01, 0.01, 0.01)]
	[InlineData(60, 40, 0, 0)]
	[InlineData(1, -0.99, 0, 0)]
	[InlineData(1.01, -0.02, 0.01, 0)]
	public void CategoryCostsSumToTheActualShareIncludingNegativeLines(double first, double second, double third, double fourth)
	{
		var row = Row([(decimal)first, (decimal)second, (decimal)third, (decimal)fourth]);
		foreach (var user in new[] { Anna, Bartek })
		{
			var entry = calculator.CreateEntry(row, user);
			Assert.Equal(row.Expense.Amount, entry.Categories.Sum(category => category.Totals.Amount));
			Assert.Equal(entry.UserShareAmount, entry.Categories.Sum(category => category.Totals.UserShareAmount));
			Assert.All(entry.Categories, category =>
				Assert.Equal(decimal.Round(category.Totals.UserShareAmount, 2), category.Totals.UserShareAmount));
			Assert.Equal(user == Anna ? row.Expense.Amount : 0m, entry.Categories.Sum(category => category.Totals.UserPaidAmount));
		}
	}

	[Fact]
	public void DiscountsFractionalQuantitiesAndSystemUnknownUseTheExpenseLineCalculation()
	{
		var row = Row([0m, 0m]);
		row.Expense.ExpenseItems.First().UnitPrice = 2.33m;
		row.Expense.ExpenseItems.First().Quantity = 1.5m;
		row.Expense.ExpenseItems.First().Discount = -0.50m;
		row.Expense.ExpenseItems.Last().UnitPrice = 1m;
		row.Expense.ExpenseItems.Last().IsSystemGenerated = true;
		row.Expense.Amount = 4m;
		var entry = calculator.CreateEntry(row, Anna);
		Assert.Equal(new[] { 3m, 1m }, entry.Categories.Select(category => category.Totals.Amount));
		Assert.Equal(2m, entry.UserShareAmount);
		Assert.Equal(2m, entry.Categories.Sum(category => category.Totals.UserShareAmount));
	}

	[Fact]
	public void PayerWithoutShareHasZeroCostAndFullPaidAmount()
	{
		var row = Row([60m, 40m]);
		var entry = calculator.CreateEntry(row with { Shares = [new ExpenseShare { FkUserId = Bartek, Percentage = 100m }] }, Anna);
		Assert.Equal(0m, entry.UserShareAmount);
		Assert.All(entry.Categories, category => Assert.Equal(0m, category.Totals.UserShareAmount));
		Assert.Equal(100m, entry.Categories.Sum(category => category.Totals.UserPaidAmount));
	}

	[Fact]
	public void CategoryCentTiesUseCategoryIdRegardlessOfItemOrder()
	{
		var row = Row([0.01m, 0.01m]);
		var entry = calculator.CreateEntry(row, Anna);
		row.Expense.ExpenseItems = row.Expense.ExpenseItems.Reverse().ToList();
		Assert.Equal(entry.Categories, calculator.CreateEntry(row, Anna).Categories);
		Assert.Equal(new[] { 0.01m, 0m }, calculator.CreateEntry(row, Anna).Categories.Select(category => category.Totals.UserShareAmount));
	}

	private static ExpenseReadRow Row(decimal[] amounts)
	{
		var expense = new Models.Expense
		{
			Id = Guid.NewGuid(),
			FkPayerId = Anna,
			Name = "Test",
			Amount = amounts.Sum(),
			TransactionTime = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
		};
		for (var index = 0; index < amounts.Length; index++)
		{
			var id = Guid.Parse($"30000000-0000-0000-0000-{index + 1:D12}");
			expense.ExpenseItems.Add(new ExpenseItem
			{
				Id = Guid.NewGuid(),
				FkCategoryId = id,
				UnitPrice = amounts[index],
				Quantity = 1m,
				FkCategory = new Category { Id = id, Name = $"Category {index}" }
			});
		}
		return new(expense, [new ExpenseShare { FkUserId = Anna, Percentage = 50m },
			new ExpenseShare { FkUserId = Bartek, Percentage = 50m }]);
	}
}
