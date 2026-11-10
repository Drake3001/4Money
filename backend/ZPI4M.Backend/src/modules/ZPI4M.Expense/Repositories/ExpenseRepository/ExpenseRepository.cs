using Microsoft.EntityFrameworkCore;
using ZPI4M.Expense.Database;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseRepository;

public class ExpenseRepository : IExpenseRepository
{
	private readonly ExpenseDbContext _context;

	public ExpenseRepository(ExpenseDbContext context)
	{
		_context = context;
	}

	public Task<bool> HasRelatedExpenseInGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default) =>
		_context.Expenses.AnyAsync(expense => expense.FkGroupId == groupId && (expense.FkPayerId == userId ||
			_context.ExpenseShares.Any(share => share.FkExpenseId == expense.Id && share.FkUserId == userId)), cancellationToken);

	public async Task<PagedResult<Models.Expense>> GetHistoryAsync(Guid actor, IReadOnlyList<Guid> activeGroupIds,
		ExpenseHistoryQuery query, CancellationToken cancellationToken = default)
	{
		var history = FilterHistory(actor, activeGroupIds, query);

		var total = await history.CountAsync(cancellationToken);
		var sorted = SortHistory(history, query);
		var ids = await sorted.ThenBy(expense => expense.Id).Skip(query.GetOffset()).Take(query.PageSize)
			.Select(expense => expense.Id).ToArrayAsync(cancellationToken);
		if (ids.Length == 0) return new([], total, query.PageNumber, query.PageSize);

		var rows = await history.Where(expense => ids.Contains(expense.Id))
			.Include(expense => expense.ExpenseItems).ThenInclude(item => item.FkCategory)
			.ToDictionaryAsync(expense => expense.Id, cancellationToken);
		var orderedRows = ids.Where(rows.ContainsKey).Select(id => rows[id]).ToArray();
		return new(orderedRows, total, query.PageNumber, query.PageSize);
	}

	private IQueryable<Models.Expense> FilterHistory(Guid actor, IReadOnlyList<Guid> activeGroupIds, ExpenseHistoryQuery query)
	{
		var history = _context.Expenses.AsNoTracking().Where(expense => activeGroupIds.Contains(expense.FkGroupId) ||
			expense.FkPayerId == actor || _context.ExpenseShares.Any(share => share.FkExpenseId == expense.Id && share.FkUserId == actor));
		if (query.GroupId is Guid groupId) history = history.Where(expense => expense.FkGroupId == groupId);
		if (query.ParticipantId is Guid participant)
			history = history.Where(expense => _context.ExpenseShares.Any(share => share.FkExpenseId == expense.Id && share.FkUserId == participant));
		if (query.PayerId is Guid payer) history = history.Where(expense => expense.FkPayerId == payer);
		if (query.TransactionTimeFrom is DateOnly startDate)
		{
			var start = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
			history = history.Where(expense => expense.TransactionTime >= start);
		}
		if (query.TransactionTimeTo is DateOnly endDate)
		{
			var end = endDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
			if (query.IncludeEndDate)
			{
				if (endDate != DateOnly.MaxValue)
				{
					end = end.AddDays(1);
					history = history.Where(expense => expense.TransactionTime < end);
				}
			}
			else history = history.Where(expense => expense.TransactionTime < end);
		}
		if (query.MinAmount.HasValue) history = history.Where(expense => expense.Amount >= query.MinAmount.Value);
		if (query.MaxAmount.HasValue) history = history.Where(expense => expense.Amount <= query.MaxAmount.Value);
		if (query.CategoryId is Guid category) history = history.Where(expense => expense.ExpenseItems.Any(item => item.FkCategoryId == category));
		if (query.HasReceipt is bool receipt) history = history.Where(expense => expense.FkReceiptId.HasValue == receipt);
		if (query.HasBankTransaction is bool bank) history = history.Where(expense => expense.FkBankTransactionId.HasValue == bank);
		if (query.Search?.Trim() is { Length: > 0 } search)
		{
			var pattern = "%" + search.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%";
			history = history.Where(expense => EF.Functions.ILike(expense.Name, pattern, "!") ||
				(expense.Merchant != null && EF.Functions.ILike(expense.Merchant, pattern, "!")));
		}

		return history;
	}

	public async Task<IReadOnlyList<ExpenseReadRow>> GetReportRowsAsync(Guid actor, IReadOnlyList<Guid> activeGroupIds,
		ExpenseHistoryQuery query, CancellationToken cancellationToken = default) =>
		await ReadRows(FilterHistory(actor, activeGroupIds, query)).ToArrayAsync(cancellationToken);

	public async Task<PagedResult<ExpenseReadRow>> GetReportPageAsync(Guid actor, IReadOnlyList<Guid> activeGroupIds,
		ExpenseHistoryQuery query, CancellationToken cancellationToken = default)
	{
		var history = FilterHistory(actor, activeGroupIds, query);
		var total = await history.CountAsync(cancellationToken);
		var sorted = SortHistory(history, query);
		var page = await ReadRows(sorted.ThenBy(expense => expense.Id).Skip(query.GetOffset()).Take(query.PageSize))
			.ToArrayAsync(cancellationToken);
		return new(page, total, query.PageNumber, query.PageSize);
	}

	private static IOrderedQueryable<Models.Expense> SortHistory(IQueryable<Models.Expense> history, ExpenseHistoryQuery query) =>
		(query.SortBy, query.Descending) switch
		{
			(ExpenseSortBy.Amount, true) => history.OrderByDescending(expense => expense.Amount),
			(ExpenseSortBy.Amount, false) => history.OrderBy(expense => expense.Amount),
			(ExpenseSortBy.Name, true) => history.OrderByDescending(expense => expense.Name),
			(ExpenseSortBy.Name, false) => history.OrderBy(expense => expense.Name),
			(ExpenseSortBy.Merchant, true) => history.OrderByDescending(expense => expense.Merchant),
			(ExpenseSortBy.Merchant, false) => history.OrderBy(expense => expense.Merchant),
			(_, true) => history.OrderByDescending(expense => expense.TransactionTime),
			_ => history.OrderBy(expense => expense.TransactionTime)
		};

	private IQueryable<ExpenseReadRow> ReadRows(IQueryable<Models.Expense> query) => query
		.Include(expense => expense.ExpenseItems).ThenInclude(item => item.FkCategory)
		.AsSingleQuery()
		.Select(expense => new ExpenseReadRow(expense,
			_context.ExpenseShares.Where(share => share.FkExpenseId == expense.Id).ToArray()));

	public void Add(Models.Expense entity)
	{
		_context.Expenses.Add(entity);
	}

	public void Remove(Models.Expense entity)
	{
		_context.Expenses.Remove(entity);
	}

	public Task<Models.Expense?> GetByIdAsync(Guid expenseId, CancellationToken cancellationToken = default)
	{
		return _context.Expenses.SingleOrDefaultAsync(e => e.Id == expenseId, cancellationToken);
	}

	public Task<Models.Expense?> GetReadAsync(Guid expenseId, CancellationToken cancellationToken = default)
	{
		return _context.Expenses.AsNoTracking().SingleOrDefaultAsync(expense => expense.Id == expenseId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid expenseId, CancellationToken cancellationToken = default)
	{
		return _context.Expenses.AnyAsync(e => e.Id == expenseId, cancellationToken);
	}
}
