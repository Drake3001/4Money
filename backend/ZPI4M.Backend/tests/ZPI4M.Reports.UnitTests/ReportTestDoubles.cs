using System.ComponentModel.DataAnnotations;
using System.Reflection;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Expense.Services.Expenses;

namespace ZPI4M.Reports.TestSupport;

public static class ReportData
{
	public static readonly Guid Actor = Guid.Parse("10000000-0000-0000-0000-000000000001");
	public static readonly Guid Group = Guid.Parse("20000000-0000-0000-0000-000000000001");
	public static readonly Guid Food = Category(1);
	public static readonly Guid Travel = Category(2);
	public static Guid Category(int number) => Guid.Parse($"30000000-0000-0000-0000-{number:D12}");

	public static IExpenseService Metadata(params Guid[] ids) => CategoryMetadataProxy.Create(_ =>
		Task.FromResult(ExpenseResult<IReadOnlyList<CategoryDetails>>.Success(ids.Distinct()
			.Select(id => new CategoryDetails(id, id == Food ? "Food" : id == Travel ? "Travel" : id.ToString(), "", "", false)).ToArray())));
}

// Proxy only for category metadata; any accidental read of individual expenses fails the test.
public class CategoryMetadataProxy : DispatchProxy
{
	public Func<CancellationToken, Task<ExpenseResult<IReadOnlyList<CategoryDetails>>>> Handler { get; set; } = null!;

	public static IExpenseService Create(Func<CancellationToken, Task<ExpenseResult<IReadOnlyList<CategoryDetails>>>> handler)
	{
		var proxy = DispatchProxy.Create<IExpenseService, CategoryMetadataProxy>();
		((CategoryMetadataProxy)(object)proxy).Handler = handler;
		return proxy;
	}

	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
		targetMethod?.Name == nameof(IExpenseService.GetCategoriesAsync)
			? Handler((CancellationToken)args![0]!)
			: throw new NotSupportedException("Reports must not read individual expenses.");
}

public sealed class ReportsSourceStub : IExpenseAggregationService
{
	public Task<ExpenseResult<ExpenseUsageTotal>> GetUsageTotalAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo,
		ExpenseAggregationScope scope, Guid? groupId, Guid? categoryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public IReadOnlyList<ExpenseCategoryBreakdownItem> Categories { get; set; } = [];
	public IReadOnlyList<ExpensePeriodCategoryTotal> Periods { get; set; } = [];
	public IReadOnlyList<ExpenseDayOfWeekTotal> Weekdays { get; set; } = [];
	public IReadOnlyList<ExpenseDailyTotalsItem> Days { get; set; } = [];
	public ExpenseErrorCode? Error { get; set; }
	public bool TransportFailure { get; set; }
	public int Calls { get; private set; }
	public Guid Actor { get; private set; }
	public DateOnly From { get; private set; }
	public DateOnly To { get; private set; }
	public ExpenseAggregationScope Scope { get; private set; }
	public Guid? GroupId { get; private set; }
	public IReadOnlyList<Guid>? CategoryIds { get; private set; }
	public ExpenseTotalsGranularity? Granularity { get; private set; }
	public CancellationToken Token { get; private set; }

	public Task<ExpenseResult<IReadOnlyList<ExpenseCategoryBreakdownItem>>> GetCategoryBreakdownAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId,
		IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default)
	{
		Capture(actor, dateFrom, dateTo, scope, groupId, categoryIds, cancellationToken);
		return Result(Categories);
	}

	public Task<ExpenseResult<IReadOnlyList<ExpensePeriodCategoryTotal>>> GetExpenseTotalsByPeriodAndCategoryAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId,
		IReadOnlyList<Guid> categoryIds, ExpenseTotalsGranularity granularity, CancellationToken cancellationToken = default)
	{
		Capture(actor, dateFrom, dateTo, scope, groupId, categoryIds, cancellationToken);
		Granularity = granularity;
		return Result(Periods);
	}

	public Task<ExpenseResult<IReadOnlyList<ExpenseDayOfWeekTotal>>> GetExpenseTotalsByDayOfWeekAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId,
		IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default)
	{
		Capture(actor, dateFrom, dateTo, scope, groupId, categoryIds, cancellationToken);
		return Result(Weekdays);
	}

	public Task<ExpenseResult<IReadOnlyList<ExpenseDailyTotalsItem>>> GetDailyExpenseTotalsAsync(
		Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId,
		IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default)
	{
		Capture(actor, dateFrom, dateTo, scope, groupId, categoryIds, cancellationToken);
		return Result(Days);
	}

	private Task<ExpenseResult<T>> Result<T>(T rows) where T : notnull => Task.FromResult(Error is { } error
		? ExpenseResult<T>.Failure(error, [new ValidationResult("Source validation", ["DateFrom"])])
		: ExpenseResult<T>.Success(rows));

	private void Capture(Guid actor, DateOnly from, DateOnly to, ExpenseAggregationScope scope, Guid? group,
		IReadOnlyList<Guid> categories, CancellationToken token)
	{
		Calls++;
		Actor = actor;
		From = from;
		To = to;
		Scope = scope;
		GroupId = group;
		CategoryIds = categories;
		Token = token;
		if (TransportFailure) throw new HttpRequestException("synthetic-private-diagnostic");
	}
}
