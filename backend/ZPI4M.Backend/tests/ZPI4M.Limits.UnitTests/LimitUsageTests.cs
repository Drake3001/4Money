using System.ComponentModel.DataAnnotations;
using Xunit;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Services.Usage;

namespace ZPI4M.Limits.UnitTests;

public sealed class LimitUsageTests
{
	private static readonly Guid Actor = Guid.NewGuid();
	private static readonly Guid Group = Guid.NewGuid();
	private static readonly Guid Category = Guid.NewGuid();
	[Theory]
	[InlineData(true, 300, 100, "100.00", "50.00", 66.67, false)]
	[InlineData(false, 300, 100, "300.00", "-150.00", 200, true)]
	[InlineData(false, 150, 100, "150.00", "0.00", 100, false)]
	[InlineData(true, 0, 0, "0.00", "150.00", 0, false)]
	[InlineData(true, 100, -15, "-15.00", "165.00", -10, false)]
	public async Task UsagePreservesScopeAndFormatsExpenseTotals(bool personal, decimal amount, decimal share, string spent, string remaining, decimal percentage, bool exceeded)
	{
		var read = new Reader
		{
			Result = ExpenseResult<ExpenseUsageTotal>.Success(new(personal ? share : amount))
		};
		var service = new LimitUsageService(read, read);
		var result = personal ? await service.ForUserAsync(Actor, Personal(), new(2026, 11, 14)) : await service.ForGroupAsync(Actor, Grouped(), new(2026, 11, 14));
		Assert.Equal(200, result.Status);
		Assert.Equal("PLN", result.Value!.CurrencyCode);
		Assert.Equal(spent, result.Value.SpentAmount);
		Assert.Equal(remaining, result.Value.RemainingAmount);
		Assert.Equal(percentage, result.Value.UsedPercentage);
		Assert.Equal(exceeded, result.Value.IsExceeded);
		Assert.Equal(Actor, read.Actor);
		Assert.Equal(Category, read.CategoryId);
		Assert.Equal(new DateOnly(2026, 11, 1), read.DateFrom);
		Assert.Equal(new DateOnly(2026, 11, 30), read.DateTo);
		Assert.Equal(personal ? ExpenseAggregationScope.Personal : ExpenseAggregationScope.Group, read.Scope);
		Assert.Equal(personal ? null : Group, read.GroupId);
	}

	[Fact]
	public async Task UnsupportedBudgetCurrencyReturnsValidationErrorWithoutReadingExpense()
	{
		var read = new Reader();
		var service = new LimitUsageService(read, read);
		var personal = Personal();
		personal.CurrencyCode = "EUR";
		var result = await service.ForUserAsync(Actor, personal, new(2026, 11, 14));
		Assert.Equal(400, result.Status);
		Assert.Null(result.Value);
		Assert.Contains("currencyCode", result.Errors!.Keys);
		Assert.Equal(0, read.Calls);
	}

	[Fact]
	public async Task MismatchedExpenseCurrencyFailsInsteadOfReturningUsage()
	{
		var read = new Reader { Result = ExpenseResult<ExpenseUsageTotal>.Success(new(100, "EUR")) };
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			new LimitUsageService(read, read).ForGroupAsync(Actor, Grouped(), new(2026, 11, 14)));
	}

	[Theory]
	[InlineData(ExpenseErrorCode.Forbidden, 404)]
	[InlineData(ExpenseErrorCode.NotFound, 404)]
	[InlineData(ExpenseErrorCode.InvalidExpense, 400)]
	[InlineData(ExpenseErrorCode.TemporarilyUnavailable, 503)]
	public async Task FailedExpenseReadPropagatesWithoutZeroFallback(ExpenseErrorCode error, int expected)
	{
		var read = new Reader
		{
			Result = ExpenseResult<ExpenseUsageTotal>.Failure(error, [new ValidationResult("Invalid date.", ["DateFrom"])])
		};
		var result = await new LimitUsageService(read, read).ForUserAsync(Actor, Personal(), new(2026, 11, 14));
		Assert.Equal(expected, result.Status);
		Assert.Null(result.Value);
		if (expected == 400)
			Assert.Contains("dateFrom", result.Errors!.Keys);
	}

	[Fact]
	public async Task SystemGroupUsesTheTrustedReaderWithoutAnActor()
	{
		var regular = new Reader { Result = ExpenseResult<ExpenseUsageTotal>.Success(new(999)) };
		var system = new Reader { Result = ExpenseResult<ExpenseUsageTotal>.Success(new(12)) };
		var result = await new LimitUsageService(regular, system).ForGroupSystemAsync(Grouped(), new(2026, 11, 14));
		Assert.Equal("12.00", result.Value!.SpentAmount);
		Assert.Equal(0, regular.Calls);
		Assert.Equal(Guid.Empty, system.Actor);
		Assert.Equal(Group, system.GroupId);
		Assert.Equal(Category, system.CategoryId);
	}

	private static UserLimit Personal() => new()
	{
		UserId = Actor,
		Amount = 150,
		CurrencyCode = "PLN",
		CategoryId = Category,
		Period = LimitPeriod.Month
	};
	private static GroupLimit Grouped() => new()
	{
		GroupId = Group,
		Amount = 150,
		CurrencyCode = "PLN",
		CategoryId = Category,
		Period = LimitPeriod.Month
	};
	private sealed class Reader : IExpenseAggregationService, IExpenseSystemAggregationService
	{
		public int Calls { get; private set; }
		public Guid Actor { get; private set; }
		public Guid? CategoryId { get; private set; }
		public Guid? GroupId { get; private set; }
		public DateOnly DateFrom { get; private set; }
		public DateOnly DateTo { get; private set; }
		public ExpenseAggregationScope Scope { get; private set; }
		public ExpenseResult<ExpenseUsageTotal> Result { get; set; } = ExpenseResult<ExpenseUsageTotal>.Success(new(0));

		public Task<ExpenseResult<ExpenseUsageTotal>> GetUsageTotalAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, Guid? categoryId, CancellationToken token = default)
		{
			Calls++;
			Actor = actor;
			CategoryId = categoryId; GroupId = groupId; DateFrom = dateFrom; DateTo = dateTo; Scope = scope;
			return Task.FromResult(Result);
		}

		public Task<ExpenseResult<IReadOnlyList<ExpenseCategoryBreakdownItem>>> GetCategoryBreakdownAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<ExpenseResult<IReadOnlyList<ExpensePeriodCategoryTotal>>> GetExpenseTotalsByPeriodAndCategoryAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, IReadOnlyList<Guid> categoryIds, ExpenseTotalsGranularity granularity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<ExpenseResult<IReadOnlyList<ExpenseDayOfWeekTotal>>> GetExpenseTotalsByDayOfWeekAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<ExpenseResult<IReadOnlyList<ExpenseDailyTotalsItem>>> GetDailyExpenseTotalsAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<ExpenseResult<ExpenseUsageTotal>> GetGroupUsageTotalAsync(Guid groupId, DateOnly dateFrom, DateOnly dateTo, Guid? categoryId, CancellationToken cancellationToken = default) =>
			GetUsageTotalAsync(Guid.Empty, dateFrom, dateTo, ExpenseAggregationScope.Group, groupId, categoryId, cancellationToken);
	}
}
