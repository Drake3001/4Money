using Microsoft.EntityFrameworkCore;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Repositories.ExpenseShareRepository;

public class ExpenseShareRepository : IExpenseShareRepository
{
	private readonly ExpenseDbContext _context;

	public ExpenseShareRepository(ExpenseDbContext context)
	{
		_context = context;
	}

	public void Add(ExpenseShare entity)
	{
		_context.ExpenseShares.Add(entity);
	}

	public void Remove(ExpenseShare entity)
	{
		_context.ExpenseShares.Remove(entity);
	}

	public void RemoveRange(IEnumerable<ExpenseShare> shares) => _context.ExpenseShares.RemoveRange(shares);

	public Task DeleteByExpenseAsync(Guid expenseId, CancellationToken cancellationToken = default) =>
		_context.ExpenseShares.Where(share => share.FkExpenseId == expenseId).ExecuteDeleteAsync(cancellationToken);

	public async Task<IReadOnlyList<ExpenseShare>> GetTrackedByExpenseAsync(Guid expenseId, CancellationToken cancellationToken = default) =>
		await _context.ExpenseShares.Where(share => share.FkExpenseId == expenseId).ToArrayAsync(cancellationToken);

	public async Task<IReadOnlyList<ExpenseShare>> GetByExpensesAsync(IReadOnlyCollection<Guid> expenseIds, CancellationToken cancellationToken = default) =>
		await _context.ExpenseShares.AsNoTracking().Where(share => expenseIds.Contains(share.FkExpenseId)).ToArrayAsync(cancellationToken);

	public Task<ExpenseShare?> GetAsync(Guid expenseId, Guid userId, CancellationToken cancellationToken = default)
	{
		return _context.ExpenseShares.SingleOrDefaultAsync(s => s.FkExpenseId == expenseId && s.FkUserId == userId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid expenseId, Guid userId, CancellationToken cancellationToken = default)
	{
		return _context.ExpenseShares.AnyAsync(s => s.FkExpenseId == expenseId && s.FkUserId == userId, cancellationToken);
	}

	public async Task<IReadOnlyList<ExpenseShare>> GetByExpenseAsync(Guid expenseId, CancellationToken cancellationToken = default)
	{
		return await _context.ExpenseShares.AsNoTracking().Where(s => s.FkExpenseId == expenseId).OrderBy(s => s.FkUserId).ToListAsync(cancellationToken);
	}

	public async Task<decimal> GetTotalPercentageAsync(Guid expenseId, CancellationToken cancellationToken = default)
	{
		return await _context.ExpenseShares.Where(s => s.FkExpenseId == expenseId).SumAsync(s => (decimal?)s.Percentage, cancellationToken) ?? 0m;
	}

	public async Task<PagedResult<ExpenseShare>> GetPageAsync(ExpenseShareQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (query.MinPercentage < 0 || query.MinPercentage > 100 || query.MaxPercentage < 0 || query.MaxPercentage > 100 || query.MinPercentage > query.MaxPercentage)
			throw new ArgumentException("Percentage bounds must be between 0 and 100, with minimum <= maximum.", nameof(query));
		IQueryable<ExpenseShare> shares = _context.ExpenseShares;
		if (query.ExpenseId.HasValue)
			shares = shares.Where(s => s.FkExpenseId == query.ExpenseId.Value);
		if (query.UserId.HasValue)
			shares = shares.Where(s => s.FkUserId == query.UserId.Value);
		if (query.MinPercentage.HasValue)
			shares = shares.Where(s => s.Percentage >= query.MinPercentage.Value);
		if (query.MaxPercentage.HasValue)
			shares = shares.Where(s => s.Percentage <= query.MaxPercentage.Value);
		IOrderedQueryable<ExpenseShare> ordered;
		switch (query.SortBy)
		{
			case ExpenseShareSortBy.ExpenseId:
				if (query.Descending)
					ordered = shares.OrderByDescending(s => s.FkExpenseId);
				else
					ordered = shares.OrderBy(s => s.FkExpenseId);
				break;
			case ExpenseShareSortBy.UserId:
				if (query.Descending)
					ordered = shares.OrderByDescending(s => s.FkUserId);
				else
					ordered = shares.OrderBy(s => s.FkUserId);
				break;
			case ExpenseShareSortBy.Percentage:
				if (query.Descending)
					ordered = shares.OrderByDescending(s => s.Percentage);
				else
					ordered = shares.OrderBy(s => s.Percentage);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(query), "Unknown sort field.");
		}
		int offset = query.GetOffset();
		int totalCount = await shares.CountAsync(cancellationToken);
		var items = await ordered.ThenBy(s => s.FkExpenseId).ThenBy(s => s.FkUserId)
			.Skip(offset)
			.Take(query.PageSize)
			.ToListAsync(cancellationToken);

		return new PagedResult<ExpenseShare>(items, totalCount, query.PageNumber, query.PageSize);
	}
}
