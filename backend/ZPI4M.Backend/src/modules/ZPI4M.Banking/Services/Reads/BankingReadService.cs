using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories.BankAccountRepository;
using ZPI4M.Banking.Repositories.BankTransactionRepository;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Services.BankingProfile;

namespace ZPI4M.Banking.Services.Reads;

public sealed class BankingReadService(IBankAccountRepository accounts, IBankTransactionRepository transactions,
	IUserBankingProfileService users) : IBankingReadService
{
	public async Task<BankingResult<PagedResult<BankAccountDetails>>> GetAccountsAsync(
		Guid userId, BankAccountReadQuery query, CancellationToken cancellationToken = default)
	{
		ValidateId(userId, nameof(userId));
		ArgumentNullException.ThrowIfNull(query);
		query.GetOffset();
		var sort = query.SortBy switch
		{
			BankAccountReadSortBy.Name => BankAccountSortBy.Name,
			BankAccountReadSortBy.CurrencyCode => BankAccountSortBy.CurrencyCode,
			BankAccountReadSortBy.IsClosed => BankAccountSortBy.IsClosed,
			_ => throw new ArgumentOutOfRangeException(nameof(query.SortBy))
		};
		var read = new BankAccountQuery
		{
			UserId = userId,
			Name = query.Name,
			CurrencyCode = query.CurrencyCode,
			Type = query.Type,
			IsClosed = query.IsClosed,
			SortBy = sort,
			Descending = query.Descending,
			PageNumber = query.PageNumber,
			PageSize = query.PageSize
		};
		var error = await CheckUserAsync(userId, cancellationToken);
		if (error.HasValue) return BankingResult<PagedResult<BankAccountDetails>>.Failure(error.Value);
		var page = await accounts.GetPageAsync(read, cancellationToken);
		return BankingResult<PagedResult<BankAccountDetails>>.Success(new(
			page.Items.Select(MapAccount).ToArray(), page.TotalCount, page.PageNumber, page.PageSize));
	}

	public async Task<BankingResult<BankAccountDetails>> GetAccountAsync(
		Guid userId, Guid accountId, CancellationToken cancellationToken = default)
	{
		ValidateId(userId, nameof(userId));
		ValidateId(accountId, nameof(accountId));
		var error = await CheckUserAsync(userId, cancellationToken);
		if (error.HasValue) return BankingResult<BankAccountDetails>.Failure(error.Value);
		var account = await accounts.GetByIdForUserAsync(userId, accountId, cancellationToken);
		return account is null ? BankingResult<BankAccountDetails>.Failure(BankingErrorCode.NotFound)
			: BankingResult<BankAccountDetails>.Success(MapAccount(account));
	}

	public async Task<BankingResult<PagedResult<BankTransactionDetails>>> GetTransactionsAsync(
		Guid userId, BankTransactionReadQuery query, CancellationToken cancellationToken = default)
	{
		ValidateId(userId, nameof(userId));
		ArgumentNullException.ThrowIfNull(query);
		query.GetOffset();
		if (query.BankAccountId.HasValue) ValidateId(query.BankAccountId.Value, nameof(query.BankAccountId));
		if (query.DateFrom > query.DateTo) throw new ArgumentException("DateFrom must not exceed DateTo.", nameof(query));
		if (query.MinAmount > query.MaxAmount) throw new ArgumentException("MinAmount must not exceed MaxAmount.", nameof(query));
		var sort = query.SortBy switch
		{
			BankTransactionReadSortBy.Date => BankTransactionSortBy.Date,
			BankTransactionReadSortBy.Amount => BankTransactionSortBy.Amount,
			BankTransactionReadSortBy.Status => BankTransactionSortBy.Status,
			BankTransactionReadSortBy.CurrencyCode => BankTransactionSortBy.CurrencyCode,
			_ => throw new ArgumentOutOfRangeException(nameof(query.SortBy))
		};
		var read = new BankTransactionQuery
		{
			UserId = userId,
			BankAccountId = query.BankAccountId,
			DateFrom = query.DateFrom,
			DateTo = query.DateTo,
			MinAmount = query.MinAmount,
			MaxAmount = query.MaxAmount,
			CurrencyCode = query.CurrencyCode,
			Description = query.Description,
			Status = query.Status,
			SortBy = sort,
			Descending = query.Descending,
			PageNumber = query.PageNumber,
			PageSize = query.PageSize
		};
		var error = await CheckUserAsync(userId, cancellationToken);
		if (error.HasValue) return BankingResult<PagedResult<BankTransactionDetails>>.Failure(error.Value);
		if (read.BankAccountId.HasValue && await accounts.GetByIdForUserAsync(userId, read.BankAccountId.Value, cancellationToken) is null)
			return BankingResult<PagedResult<BankTransactionDetails>>.Failure(BankingErrorCode.NotFound);
		var page = await transactions.GetPageAsync(read, cancellationToken);
		return BankingResult<PagedResult<BankTransactionDetails>>.Success(new(
			page.Items.Select(MapTransaction).ToArray(), page.TotalCount, page.PageNumber, page.PageSize));
	}

	public async Task<BankingResult<BankTransactionDetails>> GetTransactionAsync(
		Guid userId, Guid transactionId, CancellationToken cancellationToken = default)
	{
		ValidateId(userId, nameof(userId));
		ValidateId(transactionId, nameof(transactionId));
		var error = await CheckUserAsync(userId, cancellationToken);
		if (error.HasValue) return BankingResult<BankTransactionDetails>.Failure(error.Value);
		var transaction = await transactions.GetByIdForUserAsync(userId, transactionId, cancellationToken);
		return transaction is null ? BankingResult<BankTransactionDetails>.Failure(BankingErrorCode.NotFound)
			: BankingResult<BankTransactionDetails>.Success(MapTransaction(transaction));
	}

	private async Task<BankingErrorCode?> CheckUserAsync(Guid userId, CancellationToken cancellationToken)
	{
		var profile = await users.GetAsync(userId, cancellationToken);
		return profile is null ? BankingErrorCode.NotFound : !profile.IsActive ? BankingErrorCode.Forbidden : null;
	}

	private static void ValidateId(Guid id, string name)
	{
		if (id == Guid.Empty) throw new ArgumentException("A nonempty local ID is required.", name);
	}

	private static BankAccountDetails MapAccount(BankAccount account) => new(account.Id, account.Name, account.Number,
		account.Iban, account.FinancialInstitutionId, account.Type, account.CurrencyCode, account.IsClosed,
		account.LastSuccessfulSyncAt, account.TransactionsSyncedThrough);

	private static BankTransactionDetails MapTransaction(BankTransaction transaction) => new(transaction.Id,
		transaction.FkBankAccountId, transaction.Amount, transaction.CurrencyCode, transaction.Date,
		transaction.Description, transaction.Status);
}
