using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Services.Contracts;

namespace ZPI4M.Expense.Services.Expenses;

/// <summary>Prepares validated manual expenses without database access.</summary>
public sealed class ExpenseRequestService(IExpenseCalculationService calculations) : IExpenseRequestService
{
	private const decimal MaxMoney = 9999999999999999.99m;

	public CreateExpenseRequest Normalize(CreateExpenseRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return new()
		{
			GroupId = request.GroupId,
			BankTransactionId = request.BankTransactionId,
			PayerId = request.PayerId,
			Name = request.Name?.Trim() ?? string.Empty,
			Merchant = OptionalText(request.Merchant),
			Amount = request.Amount,
			TransactionTime = request.TransactionTime,
			Items = request.Items?.Select(item => item is null ? null! : new CreateExpenseItemRequest
			{
				Id = item.Id,
				CategoryId = item.CategoryId,
				Name = item.Name?.Trim() ?? string.Empty,
				UnitPrice = item.UnitPrice,
				Quantity = item.Quantity,
				Discount = item.Discount,
				Unit = OptionalText(item.Unit)
			}).ToArray()!,
			Shares = request.Shares
		};
	}

	public IReadOnlyList<ValidationResult> Validate(CreateExpenseRequest request) => Prepare(request).Errors;

	public ExpensePreparationResult Prepare(CreateExpenseRequest request, IReadOnlySet<Guid>? editableItemIds = null)
	{
		request = Normalize(request);
		var errors = new List<ValidationResult>();
		var items = new List<PreparedExpenseItem>();
		var shares = new List<(Guid UserId, decimal Percentage)>();
		if (request.BankTransactionId == Guid.Empty) errors.Add(new("A nonempty bank transaction ID is required.", [nameof(request.BankTransactionId)]));
		if (request.GroupId == Guid.Empty) errors.Add(new("A nonempty group ID is required.", [nameof(request.GroupId)]));
		if (request.PayerId == Guid.Empty) errors.Add(new("A nonempty payer ID is required.", [nameof(request.PayerId)]));
		if (string.IsNullOrWhiteSpace(request.Name)) errors.Add(new("Name is required.", [nameof(request.Name)]));
		if (request.TransactionTime == default)
			errors.Add(new("TransactionTime must be a date in YYYY-MM-DD format.", [nameof(request.TransactionTime)]));

		decimal? target = null;
		if (request.Amount is not null)
		{
			if (TryParseDecimal(request.Amount, 2, out var amount) && amount > 0) target = amount;
			else errors.Add(new("Amount must be positive decimal text with at most two fractional digits.", [nameof(request.Amount)]));
		}

		if (request.Items is null) errors.Add(new("Items are required.", [nameof(request.Items)]));
		else
		{
			var itemIds = new HashSet<Guid>();
			for (var index = 0; index < request.Items.Count; index++)
			{
				var item = request.Items[index];
				var field = $"{nameof(request.Items)}[{index}]";
				if (item is null) errors.Add(new("An item cannot be null.", [field]));
				else
				{
					if (item.Id is Guid id && (editableItemIds?.Contains(id) != true || !itemIds.Add(id)))
						errors.Add(new("Use an existing ordinary item ID only once.", [$"{field}.Id"]));
					if (ValidateItem(item, field, errors) is PreparedExpenseItem prepared) items.Add(prepared);
				}
			}
			if (items.Count == request.Items.Count)
			{
				var sum = items.Sum(item => item.Total);
				if (target.HasValue && sum > target.Value)
					errors.Add(new("The sum of items must not exceed Amount.", [nameof(request.Items), nameof(request.Amount)]));
				if (target.HasValue && target.Value - sum > MaxMoney)
					errors.Add(new("The system item must fit numeric(18,2).", [nameof(request.Items), nameof(request.Amount)]));
				if (request.Amount is null && (sum <= 0 || sum > MaxMoney))
					errors.Add(new("The calculated amount must be positive and fit numeric(18,2).", [nameof(request.Items)]));
			}
		}

		if (request.Shares is null || request.Shares.Count == 0)
			errors.Add(new("At least one share is required.", [nameof(request.Shares)]));
		else
		{
			var users = new HashSet<Guid>();
			for (var index = 0; index < request.Shares.Count; index++)
			{
				var share = request.Shares[index];
				var field = $"{nameof(request.Shares)}[{index}]";
				if (share is null) errors.Add(new("A share cannot be null.", [field]));
				else
				{
					if (share.UserId == Guid.Empty) errors.Add(new("A nonempty participant ID is required.", [$"{field}.UserId"]));
					if (!users.Add(share.UserId)) errors.Add(new("Participants must be unique.", [nameof(request.Shares)]));
					if (TryParseDecimal(share.Percentage, 2, out var percentage) && percentage > 0 && percentage <= 100)
					{
						shares.Add((share.UserId, percentage));
					}
					else errors.Add(new("Percentage must be greater than 0 and at most 100, with at most two fractional digits.", [$"{field}.Percentage"]));
				}
			}
			if (shares.Count == request.Shares.Count && shares.Sum(share => share.Percentage) != 100m)
				errors.Add(new("Percentages must sum to 100.", [nameof(request.Shares)]));
		}
		if (errors.Count != 0) return new(null, errors);
		var itemSum = items.Sum(item => item.Total);
		var finalAmount = target ?? itemSum;
		if (target.HasValue && target.Value > itemSum)
			items.Add(new(ExpenseSystemCategories.UnknownId, "Nieznane", target.Value - itemSum, 1m, 0m, null, target.Value - itemSum, true));
		var preparedShares = calculations.CalculateShares(finalAmount, shares);
		return new(new(request.GroupId, request.PayerId, request.Name, request.Merchant, finalAmount,
			request.TransactionTime.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), items, preparedShares), errors);
	}

	private static PreparedExpenseItem? ValidateItem(CreateExpenseItemRequest item, string field, List<ValidationResult> errors)
	{
		if (item.CategoryId == Guid.Empty) errors.Add(new("A nonempty category ID is required.", [$"{field}.CategoryId"]));
		if (item.CategoryId == ExpenseSystemCategories.UnknownId)
			errors.Add(new("The system category cannot be assigned manually.", [$"{field}.CategoryId"]));
		if (string.IsNullOrWhiteSpace(item.Name)) errors.Add(new("Name is required.", [$"{field}.Name"]));
		var validPrice = TryParseDecimal(item.UnitPrice, 2, out var price);
		var validQuantity = TryParseDecimal(item.Quantity, 3, out var quantity) && quantity > 0;
		var validDiscount = TryParseDecimal(item.Discount, 2, out var discount) && discount <= 0;
		if (!validPrice) errors.Add(new("UnitPrice must fit numeric(18,2), using decimal text.", [$"{field}.UnitPrice"]));
		if (!validQuantity) errors.Add(new("Quantity must be positive decimal text fitting numeric(18,3).", [$"{field}.Quantity"]));
		if (!validDiscount) errors.Add(new("Discount must be nonpositive decimal text fitting numeric(18,2).", [$"{field}.Discount"]));
		if (!validPrice || !validQuantity || !validDiscount) return null;
		if (TryGetTotal(price, quantity, discount, out var total))
			return new(item.CategoryId, item.Name, price, quantity, discount, item.Unit, total, false) { Id = item.Id };
		errors.Add(new("Invalid item total: check precision and discount; a standalone negative item must have Discount = 0.",
			[$"{field}.UnitPrice", $"{field}.Quantity", $"{field}.Discount"]));
		return null;
	}

	internal static bool TryGetTotal(decimal price, decimal quantity, decimal discount, out decimal total)
	{
		total = 0;
		if (price < 0 && discount != 0) return false;
		try
		{
			total = ExpenseCalculationService.CalculateItemTotal(price, quantity, discount);
			return Math.Abs(total) <= MaxMoney && (price < 0 || total >= 0);
		}
		catch (OverflowException)
		{
			return false;
		}
	}

	public static bool TryParseDecimal(string? text, int scale, out decimal number)
	{
		number = 0;
		if (string.IsNullOrEmpty(text)) return false;
		var unsigned = text.StartsWith('-') ? text[1..] : text;
		var parts = unsigned.Split('.');
		if (parts.Length > 2 || parts[0].Length == 0 || parts[0].Length > 18 - scale ||
			parts.Any(part => part.Length == 0 || part.Any(character => character is < '0' or > '9')) ||
			parts.Length == 2 && parts[1].Length > scale)
			return false;
		return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
			CultureInfo.InvariantCulture, out number);
	}

	private static string? OptionalText(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
