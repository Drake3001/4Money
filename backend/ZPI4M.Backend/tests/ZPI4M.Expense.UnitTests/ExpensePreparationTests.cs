using System.Globalization;
using Xunit;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Expenses;

namespace ZPI4M.Expense.UnitTests;

public sealed class ExpensePreparationTests
{
	private static readonly Guid UserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
	private readonly ExpenseRequestService _service = new(new ExpenseCalculationService());

	[Fact]
	public void PurchaseDateIsPreparedAsMidnightUtc()
	{
		var result = _service.Prepare(Request("10"));
		var prepared = Assert.IsType<PreparedExpense>(result.Value);
		Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), prepared.TransactionTime);
		Assert.Equal(DateTimeKind.Utc, prepared.TransactionTime.Kind);
	}

	[Fact]
	public void MissingTargetCalculatesItemsWithoutAddingUnknown()
	{
		var result = _service.Prepare(Request(null, [Item("10.00", "2", "-3"), Item("-2.00")]));
		Assert.Empty(result.Errors);
		var expense = Assert.IsType<PreparedExpense>(result.Value);
		Assert.Equal(15m, expense.Amount);
		Assert.Equal(2, expense.Items.Count);
		Assert.All(expense.Items, item => Assert.False(item.IsSystemGenerated));
		Assert.Equal(15m, Assert.Single(expense.Shares).Amount);
	}

	[Theory]
	[InlineData("100", "70", "30")]
	[InlineData("100", "-20", "120")]
	public void ExplicitTargetAddsOnlyTheMissingAmount(string target, string price, string expected)
	{
		var result = _service.Prepare(Request(target, [Item(price)]));
		Assert.Empty(result.Errors);
		Assert.NotNull(result.Value);
		var unknown = Assert.Single(result.Value.Items, item => item.IsSystemGenerated);
		Assert.Equal(ExpenseSystemCategories.UnknownId, unknown.CategoryId);
		Assert.Equal("Nieznane", unknown.Name);
		Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), unknown.UnitPrice);
		Assert.Equal(unknown.UnitPrice, unknown.Total);
		Assert.Equal(1m, unknown.Quantity);
		Assert.Equal(0m, unknown.Discount);
		Assert.Null(unknown.Unit);
		Assert.Equal(result.Value.Amount, result.Value.Items.Sum(item => item.Total));
	}

	[Fact]
	public void TargetWithoutItemsIsEntirelyUnknownAndExactSumAddsNothing()
	{
		var empty = _service.Prepare(Request("100"));
		Assert.Empty(empty.Errors);
		Assert.NotNull(empty.Value);
		Assert.True(Assert.Single(empty.Value.Items).IsSystemGenerated);
		var exact = _service.Prepare(Request("10.00", [Item("10")]));
		Assert.Empty(exact.Errors);
		Assert.NotNull(exact.Value);
		Assert.False(Assert.Single(exact.Value.Items).IsSystemGenerated);
	}

	[Theory]
	[InlineData("9999999999999999.99", "-0.01")]
	[InlineData("0.01", "-9999999999999999.99")]
	public void UnknownDifferenceMustFitMoneyPrecision(string target, string price)
	{
		var result = _service.Prepare(Request(target, [Item(price)]));
		Assert.Null(result.Value);
		Assert.Contains(result.Errors, error => error.MemberNames.Contains("Amount"));
	}

	[Fact]
	public void PreparationRejectsManualSystemCategoryButNotAnOrdinaryItemName()
	{
		var request = Request("10", [new()
		{
			CategoryId = ExpenseSystemCategories.UnknownId,
			Name = "Product",
			UnitPrice = "10",
			Quantity = "1"
		}]);
		var rejected = _service.Prepare(request);
		Assert.Null(rejected.Value);
		Assert.Contains(rejected.Errors, error => error.MemberNames.Contains("Items[0].CategoryId"));
		var ordinary = _service.Prepare(Request(null, [new()
		{
			CategoryId = Guid.NewGuid(),
			Name = "Nieznane",
			UnitPrice = "10",
			Quantity = "1"
		}]));
		Assert.Empty(ordinary.Errors);
		Assert.NotNull(ordinary.Value);
		Assert.False(Assert.Single(ordinary.Value.Items).IsSystemGenerated);
	}

	[Fact]
	public void PreparationNormalizesTextWithoutChangingInputAndParsesDecimals()
	{
		var request = Request(null, [Item("10.00", "0.500", "-0.50")]);
		var result = _service.Prepare(request);
		Assert.Empty(result.Errors);
		Assert.NotNull(result.Value);
		Assert.Equal(" Expense ", request.Name);
		Assert.Equal(" ", request.Merchant);
		Assert.Equal(" Product ", request.Items[0].Name);
		Assert.Equal(" kg ", request.Items[0].Unit);
		Assert.Equal("Expense", result.Value.Name);
		Assert.Null(result.Value.Merchant);
		var item = Assert.Single(result.Value.Items);
		Assert.Equal("Product", item.Name);
		Assert.Equal("kg", item.Unit);
		Assert.Equal(10m, item.UnitPrice);
		Assert.Equal(0.5m, item.Quantity);
		Assert.Equal(-0.5m, item.Discount);
		Assert.Equal(4.5m, item.Total);
	}

	[Fact]
	public void EditPreparationPreservesItemIdsAndLeavesNewAndSystemItemsWithoutIds()
	{
		var firstId = Guid.NewGuid();
		var secondId = Guid.NewGuid();
		var request = Request("10", [Item("3", id: secondId), Item("2", id: firstId), Item("1")]);
		var result = _service.Prepare(request, new HashSet<Guid> { firstId, secondId });

		Assert.Empty(result.Errors);
		var prepared = Assert.IsType<PreparedExpense>(result.Value);
		Assert.Equal(new Guid?[] { secondId, firstId, null, null }, prepared.Items.Select(item => item.Id));
		Assert.True(prepared.Items[^1].IsSystemGenerated);
		Assert.Equal(4m, prepared.Items[^1].Total);
	}

	private static CreateExpenseRequest Request(string? amount, IReadOnlyList<CreateExpenseItemRequest>? items = null) => new()
	{
		GroupId = Guid.NewGuid(),
		PayerId = UserId,
		Name = " Expense ",
		Merchant = " ",
		Amount = amount,
		TransactionTime = new DateOnly(2026, 11, 1),
		Items = items ?? [],
		Shares = [new() { UserId = UserId, Percentage = "100" }]
	};

	private static CreateExpenseItemRequest Item(string price, string quantity = "1", string discount = "0", Guid? id = null) => new()
	{
		Id = id,
		CategoryId = Guid.NewGuid(),
		Name = " Product ",
		UnitPrice = price,
		Quantity = quantity,
		Discount = discount,
		Unit = " kg "
	};
}
