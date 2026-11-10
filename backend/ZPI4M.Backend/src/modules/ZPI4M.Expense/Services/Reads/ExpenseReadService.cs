using System.ComponentModel.DataAnnotations;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Reports;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Services.Reads;

public sealed class ExpenseReadService(IExpenseRepository expenses, IGroupExpenseMetadataLookup groups,
	ExpenseReportCalculator calculations) : IExpenseReadService
{
	public async Task<ExpenseResult<PagedResult<ExpenseReportEntry>>> GetExpensesAsync(Guid actor, ExpenseReportQuery query,
		CancellationToken cancellationToken = default)
	{
		var errors = Validate(query.Filter);
		Validator.TryValidateObject(query, new ValidationContext(query), errors, true);
		if (!Enum.IsDefined(query.SortBy)) errors.Add(new("Unknown sort field.", [nameof(query.SortBy)]));
		if (errors.Count > 0) return ExpenseResult<PagedResult<ExpenseReportEntry>>.Failure(ExpenseErrorCode.InvalidExpense, errors);
		var scope = await AuthorizeAsync(actor, query.Filter, cancellationToken);
		if (!scope.IsSuccess) return ExpenseResult<PagedResult<ExpenseReportEntry>>.Failure(scope.Error!.Value);
		var page = await expenses.GetReportPageAsync(actor, scope.Value!, ToQuery(query.Filter, actor, query), cancellationToken);
		var entries = page.Items.Select(row => calculations.CreateEntry(row, query.Filter.UserId ?? actor)).ToArray();
		return ExpenseResult<PagedResult<ExpenseReportEntry>>.Success(new(entries, page.TotalCount, page.PageNumber, page.PageSize));
	}

	public async Task<ExpenseResult<ExpenseReportTotals>> GetTotalsAsync(Guid actor, ExpenseReportFilter filter,
		CancellationToken cancellationToken = default)
	{
		var read = await ReadAsync(actor, filter, cancellationToken);
		return read.IsSuccess
			? ExpenseResult<ExpenseReportTotals>.Success(ExpenseReportCalculator.Total(read.Value!, filter.UserId ?? actor, filter.CategoryId))
			: ExpenseResult<ExpenseReportTotals>.Failure(read.Error!.Value, read.ValidationErrors);
	}

	public async Task<ExpenseResult<IReadOnlyList<ExpenseCategoryTotal>>> GetCategoryTotalsAsync(Guid actor, ExpenseReportFilter filter,
		ExpenseCategorySortBy sortBy = ExpenseCategorySortBy.Amount, bool descending = true,
		CancellationToken cancellationToken = default)
	{
		if (!Enum.IsDefined(sortBy))
			return ExpenseResult<IReadOnlyList<ExpenseCategoryTotal>>.Failure(ExpenseErrorCode.InvalidExpense,
				[new("Unknown category sort field.", [nameof(sortBy)])]);
		var read = await ReadAsync(actor, filter, cancellationToken);
		if (!read.IsSuccess) return ExpenseResult<IReadOnlyList<ExpenseCategoryTotal>>.Failure(read.Error!.Value, read.ValidationErrors);
		var categories = read.Value!.SelectMany(entry => entry.Categories)
			.Where(category => filter.CategoryId is null || category.CategoryId == filter.CategoryId)
			.GroupBy(category => new { category.CategoryId, category.CategoryName })
			.Select(group => new ExpenseCategoryTotal(group.Key.CategoryId, group.Key.CategoryName,
				ExpenseReportCalculator.Sum(group.Select(category => category.Totals))));
		var sorted = (sortBy, descending) switch
		{
			(ExpenseCategorySortBy.Name, true) => categories.OrderByDescending(category => category.CategoryName, StringComparer.Ordinal),
			(ExpenseCategorySortBy.Name, false) => categories.OrderBy(category => category.CategoryName, StringComparer.Ordinal),
			(ExpenseCategorySortBy.UserShareAmount, true) => categories.OrderByDescending(category => category.Totals.UserShareAmount),
			(ExpenseCategorySortBy.UserShareAmount, false) => categories.OrderBy(category => category.Totals.UserShareAmount),
			(ExpenseCategorySortBy.UserPaidAmount, true) => categories.OrderByDescending(category => category.Totals.UserPaidAmount),
			(ExpenseCategorySortBy.UserPaidAmount, false) => categories.OrderBy(category => category.Totals.UserPaidAmount),
			(_, true) => categories.OrderByDescending(category => category.Totals.Amount),
			_ => categories.OrderBy(category => category.Totals.Amount)
		};
		return ExpenseResult<IReadOnlyList<ExpenseCategoryTotal>>.Success(sorted.ThenBy(category => category.CategoryId).ToArray());
	}

	public async Task<ExpenseResult<IReadOnlyList<ExpensePeriodTotal>>> GetTimelineAsync(Guid actor, ExpenseTimelineQuery query,
		CancellationToken cancellationToken = default)
	{
		if (!Enum.IsDefined(query.Period))
			return ExpenseResult<IReadOnlyList<ExpensePeriodTotal>>.Failure(ExpenseErrorCode.InvalidExpense,
				[new("Unknown period.", [nameof(query.Period)])]);
		var read = await ReadAsync(actor, query.Filter, cancellationToken);
		if (!read.IsSuccess) return ExpenseResult<IReadOnlyList<ExpensePeriodTotal>>.Failure(read.Error!.Value, read.ValidationErrors);
		var result = new List<ExpensePeriodTotal>();
		foreach (var period in read.Value!.GroupBy(entry => PeriodStart(entry.Date, query.Period)).OrderBy(group => group.Key))
		{
			if (!query.ByCategory)
			{
				result.Add(new(period.Key, null, null,
					ExpenseReportCalculator.Total(period, query.Filter.UserId ?? actor, query.Filter.CategoryId)));
				continue;
			}
			foreach (var category in period.SelectMany(entry => entry.Categories)
				.Where(category => query.Filter.CategoryId is null || category.CategoryId == query.Filter.CategoryId)
				.GroupBy(category => new { category.CategoryId, category.CategoryName }).OrderBy(group => group.Key.CategoryId))
				result.Add(new(period.Key, category.Key.CategoryId, category.Key.CategoryName,
					ExpenseReportCalculator.Sum(category.Select(value => value.Totals))));
		}
		return ExpenseResult<IReadOnlyList<ExpensePeriodTotal>>.Success(result);
	}

	private async Task<ExpenseResult<IReadOnlyList<ExpenseReportEntry>>> ReadAsync(Guid actor, ExpenseReportFilter filter, CancellationToken token)
	{
		var errors = Validate(filter);
		if (errors.Count > 0) return ExpenseResult<IReadOnlyList<ExpenseReportEntry>>.Failure(ExpenseErrorCode.InvalidExpense, errors);
		var scope = await AuthorizeAsync(actor, filter, token);
		if (!scope.IsSuccess) return ExpenseResult<IReadOnlyList<ExpenseReportEntry>>.Failure(scope.Error!.Value);
		var rows = await expenses.GetReportRowsAsync(actor, scope.Value!, ToQuery(filter, actor), token);
		return ExpenseResult<IReadOnlyList<ExpenseReportEntry>>.Success(rows.Select(row => calculations.CreateEntry(row, filter.UserId ?? actor)).ToArray());
	}

	private async Task<ExpenseResult<IReadOnlyList<Guid>>> AuthorizeAsync(Guid actor, ExpenseReportFilter filter, CancellationToken token)
	{
		if (actor == Guid.Empty) return ExpenseResult<IReadOnlyList<Guid>>.Failure(ExpenseErrorCode.Forbidden);
		var active = await groups.GetActiveGroupIdsAsync(actor, token);
		if (filter.GroupId is Guid id && !active.Contains(id) && !await expenses.HasRelatedExpenseInGroupAsync(actor, id, token))
			return ExpenseResult<IReadOnlyList<Guid>>.Failure(ExpenseErrorCode.NotFound);
		return ExpenseResult<IReadOnlyList<Guid>>.Success(active);
	}

	private static List<ValidationResult> Validate(ExpenseReportFilter filter)
	{
		ArgumentNullException.ThrowIfNull(filter);
		var errors = new List<ValidationResult>();
		if (new[] { filter.UserId, filter.GroupId, filter.ParticipantId, filter.PayerId, filter.CategoryId }.Contains(Guid.Empty))
			errors.Add(new("Filter IDs must be nonempty."));
		if (filter.DateFrom > filter.DateTo) errors.Add(new("DateFrom must not exceed DateTo.", [nameof(filter.DateFrom)]));
		if (filter.MinAmount < 0 || filter.MaxAmount < 0 || filter.MinAmount > filter.MaxAmount ||
			filter.MinAmount > 9999999999999999.99m || filter.MaxAmount > 9999999999999999.99m ||
			filter.MinAmount is decimal min && decimal.Round(min, 2) != min ||
			filter.MaxAmount is decimal max && decimal.Round(max, 2) != max)
			errors.Add(new("Use an increasing nonnegative amount range fitting numeric(18,2).", [nameof(filter.MinAmount), nameof(filter.MaxAmount)]));
		if (filter.Search?.Length > 200) errors.Add(new("Search must not exceed 200 characters.", [nameof(filter.Search)]));
		return errors;
	}

	private static ExpenseHistoryQuery ToQuery(ExpenseReportFilter filter, Guid actor, ExpenseReportQuery? page = null) => new()
	{
		GroupId = filter.GroupId,
		ParticipantId = filter.ParticipantId ?? (filter.GroupId is null && filter.PayerId is null ? filter.UserId ?? actor : (Guid?)null),
		PayerId = filter.PayerId,
		TransactionTimeFrom = filter.DateFrom,
		TransactionTimeTo = filter.DateTo,
		IncludeEndDate = true,
		MinAmount = filter.MinAmount,
		MaxAmount = filter.MaxAmount,
		CategoryId = filter.CategoryId,
		HasReceipt = filter.HasReceipt,
		HasBankTransaction = filter.HasBankTransaction,
		Search = filter.Search,
		SortBy = page?.SortBy switch
		{
			ExpenseReportSortBy.Amount => ExpenseSortBy.Amount,
			ExpenseReportSortBy.Name => ExpenseSortBy.Name,
			ExpenseReportSortBy.Merchant => ExpenseSortBy.Merchant,
			_ => ExpenseSortBy.TransactionTime
		},
		Descending = page?.Descending ?? true,
		PageNumber = page?.PageNumber ?? 1,
		PageSize = page?.PageSize ?? 20
	};

	private static DateOnly PeriodStart(DateOnly date, ExpenseReportPeriod period) => period switch
	{
		ExpenseReportPeriod.Year => new(date.Year, 1, 1),
		ExpenseReportPeriod.Month => new(date.Year, date.Month, 1),
		_ => date
	};
}
