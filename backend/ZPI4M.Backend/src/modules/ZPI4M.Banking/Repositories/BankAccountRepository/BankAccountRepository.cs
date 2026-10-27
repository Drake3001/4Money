using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.Repositories.BankAccountRepository;

public class BankAccountRepository : IBankAccountRepository
{
	private readonly BankingDbContext _context;

	public BankAccountRepository(BankingDbContext context)
	{
		_context = context;
	}

	public void Add(BankAccount entity)
	{
		_context.BankAccounts.Add(entity);
	}

	public void Remove(BankAccount entity)
	{
		_context.BankAccounts.Remove(entity);
	}

	public Task<BankAccount?> GetByIdAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
	{
		return _context.BankAccounts.SingleOrDefaultAsync(e => e.Id == bankAccountId, cancellationToken);
	}

	public Task<BankAccount?> GetByIdForUserAsync(Guid userId, Guid bankAccountId, CancellationToken cancellationToken = default)
	{
		return _context.BankAccounts.AsNoTracking().SingleOrDefaultAsync(
			e => e.Id == bankAccountId && e.FkUserId == userId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
	{
		return _context.BankAccounts.AnyAsync(e => e.Id == bankAccountId, cancellationToken);
	}

	public Task<BankAccount?> GetByAccountDetailsAsync(Guid userId, string financialInstitutionId, string number, CancellationToken cancellationToken = default)
	{
		return _context.BankAccounts.SingleOrDefaultAsync(a => a.FkUserId == userId &&
			a.FinancialInstitutionId == financialInstitutionId && a.Number == number, cancellationToken);
	}

	public async Task<PagedResult<BankAccount>> GetPageAsync(BankAccountQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		IQueryable<BankAccount> itemsQuery = _context.BankAccounts.AsNoTracking();
		if (query.UserId.HasValue)
			itemsQuery = itemsQuery.Where(e => e.FkUserId == query.UserId.Value);
		if (!string.IsNullOrWhiteSpace(query.Name))
		{
			var name = query.Name.Trim().ToLowerInvariant();
			itemsQuery = itemsQuery.Where(e => e.Name.ToLower().Contains(name));
		}
		if (!string.IsNullOrWhiteSpace(query.Number))
		{
			var number = query.Number.Trim();
			itemsQuery = itemsQuery.Where(e => e.Number == number);
		}
		if (!string.IsNullOrWhiteSpace(query.Iban))
		{
			var iban = query.Iban.Trim();
			itemsQuery = itemsQuery.Where(e => e.Iban == iban);
		}
		if (!string.IsNullOrWhiteSpace(query.FinancialInstitutionId))
		{
			var financialInstitutionId = query.FinancialInstitutionId.Trim();
			itemsQuery = itemsQuery.Where(e => e.FinancialInstitutionId == financialInstitutionId);
		}
		if (!string.IsNullOrWhiteSpace(query.CurrencyCode))
		{
			var currencyCode = query.CurrencyCode.Trim().ToUpperInvariant();
			itemsQuery = itemsQuery.Where(e => e.CurrencyCode.ToUpper() == currencyCode);
		}
		if (!string.IsNullOrWhiteSpace(query.Type))
		{
			var type = query.Type.Trim();
			itemsQuery = itemsQuery.Where(e => e.Type == type);
		}
		if (query.IsClosed.HasValue)
			itemsQuery = itemsQuery.Where(e => e.IsClosed == query.IsClosed.Value);

		IOrderedQueryable<BankAccount> ordered;
		switch (query.SortBy)
		{
			case BankAccountSortBy.Name:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.Name);
				else
					ordered = itemsQuery.OrderBy(e => e.Name);
				break;
			case BankAccountSortBy.Number:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.Number);
				else
					ordered = itemsQuery.OrderBy(e => e.Number);
				break;
			case BankAccountSortBy.FinancialInstitutionId:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.FinancialInstitutionId);
				else
					ordered = itemsQuery.OrderBy(e => e.FinancialInstitutionId);
				break;
			case BankAccountSortBy.CurrencyCode:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.CurrencyCode);
				else
					ordered = itemsQuery.OrderBy(e => e.CurrencyCode);
				break;
			case BankAccountSortBy.IsClosed:
				if (query.Descending)
					ordered = itemsQuery.OrderByDescending(e => e.IsClosed);
				else
					ordered = itemsQuery.OrderBy(e => e.IsClosed);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(query), "Unknown sort field.");
		}

		int offset = query.GetOffset();
		int totalCount = await itemsQuery.CountAsync(cancellationToken);
		var items = await ordered.ThenBy(e => e.Id)
			.Skip(offset)
			.Take(query.PageSize)
			.ToListAsync(cancellationToken);

		return new PagedResult<BankAccount>(items, totalCount, query.PageNumber, query.PageSize);
	}
}
