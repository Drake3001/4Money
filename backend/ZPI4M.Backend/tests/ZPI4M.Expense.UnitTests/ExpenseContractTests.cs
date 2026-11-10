using System.Globalization;
using System.Text.Json;
using Xunit;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Expenses;

namespace ZPI4M.Expense.UnitTests;

public sealed class ExpenseContractTests
{
	private static readonly Guid UserId = Guid.Parse("10000000-0000-0000-0000-000000000001");

	[Fact]
	public void ExplicitTargetAllowsMissingItemsAndPartiallyExplainedAmount()
	{
		Assert.True(IsValid(Request("100.00")));
		Assert.True(IsValid(Request("100.00", [Item("10.00", "2", "-3.00")])));
		Assert.False(IsValid(Request("16.99", [Item("10.00", "2", "-3.00")])));
	}

	[Fact]
	public void MissingAmountRequiresPositiveItemsSumAndAllowsStandaloneDiscounts()
	{
		Assert.True(IsValid(Request(null, [Item("10.00"), Item("-2.00")])));
		Assert.False(IsValid(Request(null)));
		Assert.False(IsValid(Request(null, [Item("0.00")])));
		Assert.False(IsValid(Request(null, [Item("-2.00")])));
	}

	[Fact]
	public void ItemTotalRoundsAwayFromZeroBeforeApplyingWholeLineDiscount()
	{
		var item = Item("0.01", "0.500");
		Assert.True(IsValid(Request("0.01", [item])));
		Assert.False(IsValid(Request("0.01", [item, item])));
		Assert.True(IsValid(Request("17.00", [Item("10.00", "2.000", "-3.00")])));
	}

	[Theory]
	[InlineData("1,50")]
	[InlineData("1e2")]
	[InlineData("+1.00")]
	[InlineData(" 1.00 ")]
	[InlineData("1.001")]
	[InlineData("10000000000000000.00")]
	[InlineData("0.00")]
	[InlineData("-1.00")]
	[InlineData("")]
	public void TargetRejectsInvalidPrecisionFormatRangeAndNonpositiveAmounts(string amount)
	{
		Assert.False(IsValid(Request(amount)));
	}

	[Fact]
	public void DecimalTextDoesNotDependOnCurrentCulture()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
			Assert.True(IsValid(Request("1.50", [Item("1.50")])));
			Assert.False(IsValid(Request("1,50")));
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	[Fact]
	public void ItemsAcceptFreeProductsAndRejectOversizedOrDoubleDiscounts()
	{
		Assert.True(IsValid(Request("10", [Item("0.00")])));
		Assert.False(IsValid(Request("10", [Item("10", "1", "-10.01")])));
		Assert.False(IsValid(Request("10", [Item("10", "1", "1")])));
		Assert.False(IsValid(Request("10", [Item("-2", "1", "-1")])));
		Assert.False(IsValid(Request("10", [Item("1", "0")])));
		Assert.False(IsValid(Request("10", [Item("1", "0.0001")])));
		Assert.False(IsValid(Request("10", [Item("9999999999999999.99", "999999999999999.999")])));
	}

	[Fact]
	public void ParticipantsMustBeUniqueWithPositivePercentagesSummingToOneHundred()
	{
		Assert.False(IsValid(Request("10", shares: [])));
		Assert.False(IsValid(Request("10", shares: [Share(UserId, "50"), Share(UserId, "50")])));
		Assert.False(IsValid(Request("10", shares: [Share(UserId, "99.99")])));
		Assert.False(IsValid(Request("10", shares: [Share(UserId, "0"), Share(Guid.NewGuid(), "100")])));
		Assert.False(IsValid(Request("10", shares: [Share(UserId, "100.001")])));
		Assert.True(IsValid(Request("10", shares:
			[Share(UserId, "33.33"), Share(Guid.NewGuid(), "33.33"), Share(Guid.NewGuid(), "33.34")])));
	}

	[Fact]
	public void RequestsRejectMissingIdentityDateAndNullChildren()
	{
		Assert.False(IsValid(new CreateExpenseRequest { Name = "Test", Amount = "10", TransactionTime = DateOnly.FromDateTime(DateTime.UtcNow), Shares = [Share(UserId, "100")] }));
		Assert.False(IsValid(Request("10", time: default(DateOnly))));
		Assert.False(IsValid(Request("10", [null!])));
		Assert.False(IsValid(Request("10", shares: [null!])));
		Assert.False(IsValid(Request("10", shares: [Share(Guid.Empty, "100")])));
		Assert.True(IsValid(Request("10", time: DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1))));
	}

	[Fact]
	public void JsonRequiresDecimalStringsAndPurchaseTime()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateExpenseRequest>("{}", options));
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateExpenseRequest>(
			"""{"amount":10.00,"transactionTime":"2026-11-01T12:00:00Z"}""", options));
		var request = Request("10");
		var json = JsonSerializer.Serialize(request, options);
		Assert.Contains("\"amount\":\"10\"", json);
		using var document = JsonDocument.Parse(json);
		Assert.False(document.RootElement.TryGetProperty("userId", out _));
		Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("bankTransactionId").ValueKind);
		Assert.False(document.RootElement.TryGetProperty("receiptId", out _));
	}

	[Fact]
	public void DtosKeepRawInputAndServiceNormalizesWithoutMutatingIt()
	{
		var item = new CreateExpenseItemRequest
		{
			CategoryId = Guid.NewGuid(),
			Name = " Product ",
			UnitPrice = "10.00",
			Quantity = "1.000",
			Unit = " kg "
		};
		var request = Request("10.00", [item]);
		var normalized = new ExpenseRequestService(new ExpenseCalculationService()).Normalize(request);
		Assert.Equal(" Test purchase ", request.Name);
		Assert.Equal(" ", request.Merchant);
		Assert.Equal(" Product ", item.Name);
		Assert.Equal(" kg ", item.Unit);
		Assert.Equal("Test purchase", normalized.Name);
		Assert.Null(normalized.Merchant);
		Assert.Equal("Product", normalized.Items[0].Name);
		Assert.Equal("kg", normalized.Items[0].Unit);
		Assert.Equal("10.00", normalized.Items[0].UnitPrice);
		Assert.True(IsValid(normalized));
	}

	[Fact]
	public void ServiceValidatesMissingCollectionsAndNestedFieldsDirectly()
	{
		var missing = new CreateExpenseRequest
		{
			GroupId = Guid.NewGuid(),
			PayerId = UserId,
			Name = " ",
			Amount = "10.00",
			TransactionTime = DateOnly.FromDateTime(DateTime.UtcNow),
			Items = null!,
			Shares = null!
		};
		var service = new ExpenseRequestService(new ExpenseCalculationService());
		var missingFields = service.Validate(missing).SelectMany(error => error.MemberNames).ToArray();
		Assert.Contains("Name", missingFields);
		Assert.Contains("Items", missingFields);
		Assert.Contains("Shares", missingFields);
		var request = Request("10.00", [new CreateExpenseItemRequest
		{
			CategoryId = Guid.Empty, Name = " ", UnitPrice = "10.00", Quantity = "0"
		}], [Share(Guid.Empty, "0")]);
		var fields = service.Validate(request).SelectMany(error => error.MemberNames).ToArray();
		Assert.Contains("Items[0].CategoryId", fields);
		Assert.Contains("Items[0].Name", fields);
		Assert.Contains("Items[0].Quantity", fields);
		Assert.Contains("Shares[0].UserId", fields);
		Assert.Contains("Shares[0].Percentage", fields);
	}

	[Fact]
	public void ResponseKeepsExactAmountsAndContainsItemsAndSharesWithoutEfEntities()
	{
		var response = new ExpenseResponse(Guid.NewGuid(), Guid.NewGuid(), "Dom", UserId, "Anna Testowa", "Test", null,
			"9999999999999999.99", "PLN", DateOnly.FromDateTime(DateTime.UtcNow), null, null,
			[new ExpenseItemResponse(Guid.NewGuid(), Guid.NewGuid(), "Zakupy", "shopping-cart", "Test", "1.00", "0.735", "0.00", "kg", "0.74", false)],
			[new ExpenseShareResponse(UserId, "Anna Testowa", "100.00", "9999999999999999.99")], "opaque-v1", true, true);
		using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
		Assert.Equal(response.TransactionTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), json.RootElement.GetProperty("transactionTime").GetString());
		Assert.Equal("9999999999999999.99", json.RootElement.GetProperty("amount").GetString());
		Assert.Equal("0.735", json.RootElement.GetProperty("items")[0].GetProperty("quantity").GetString());
		Assert.Equal("100.00", json.RootElement.GetProperty("shares")[0].GetProperty("percentage").GetString());
		var details = new ExpenseDetails(response.Id, response.GroupId, UserId, "Test", null,
			9999999999999999.99m, response.TransactionTime.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), null, null,
			[new ExpenseItemDetails(response.Items[0].Id, response.Items[0].CategoryId, "Test", 1m, 0.735m, 0m, "kg", 0.74m, false)],
			[new ExpenseShareDetails(UserId, 100m, 9999999999999999.99m)]);
		var success = ExpenseResult<ExpenseDetails>.Success(details);
		Assert.True(success.IsSuccess);
		Assert.Same(details, success.Value);
		Assert.Null(success.Error);
		var failure = ExpenseResult<ExpenseDetails>.Failure(ExpenseErrorCode.NotFound);
		Assert.False(failure.IsSuccess);
		Assert.Null(failure.Value);
		Assert.Equal(ExpenseErrorCode.NotFound, failure.Error);
	}

	private static bool IsValid(CreateExpenseRequest request) => new ExpenseRequestService(new ExpenseCalculationService()).Validate(request).Count == 0;

	private static CreateExpenseRequest Request(string? amount, IReadOnlyList<CreateExpenseItemRequest>? items = null,
		IReadOnlyList<CreateExpenseShareRequest>? shares = null, DateOnly? time = null) => new()
		{
			GroupId = Guid.NewGuid(),
			PayerId = UserId,
			Name = " Test purchase ",
			Merchant = " ",
			Amount = amount,
			TransactionTime = time ?? DateOnly.FromDateTime(DateTime.UtcNow),
			Items = items ?? [],
			Shares = shares ?? [Share(UserId, "100.00")]
		};

	private static CreateExpenseItemRequest Item(string price, string quantity = "1.000", string discount = "0.00") => new()
	{
		CategoryId = Guid.NewGuid(),
		Name = "Test product",
		UnitPrice = price,
		Quantity = quantity,
		Discount = discount
	};

	private static CreateExpenseShareRequest Share(Guid userId, string percentage) => new() { UserId = userId, Percentage = percentage };
}
