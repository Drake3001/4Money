using System.ComponentModel.DataAnnotations;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Services.Assignments;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseItemRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Repositories.ExpenseShareRepository;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Reads;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Expenses;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Receipt.Database;
using ZPI4M.Receipt.Services;
using ZPI4M.Shared.Pagination;
using ReceiptRecord = ZPI4M.Receipt.Models.Receipt;

namespace ZPI4M.Expense.Services.Expenses;

public sealed partial class ExpenseService(ExpenseDbContext db, IExpenseRepository expenses,
	IExpenseItemRepository items, IExpenseShareRepository shares, ICategoryRepository categories,
	IGroupMemberService members, IExpenseRequestService requests, ExpenseQueryService queries,
	IBankingReadService banking, IGroupExpenseMetadataLookup groups,
	IGroupingTransactionCoordinator transactions, IGroupExpenseContributionService groupExpenses,
	IGroupExpenseNotificationService groupNotifications, IReceiptService receipts,
	ReceiptDbContext receiptDb, ReceiptOcrProof ocrProof,
	BankingDbContext bankingDb, IBankTransactionAssignmentService bankAssignments) : IExpenseService
{
	public Task<ExpenseResult<IReadOnlyList<CategoryDetails>>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
		queries.GetCategoriesAsync(cancellationToken);

	public Task<ExpenseResult<PagedResult<ExpenseHistoryEntry>>> ListAsync(Guid actor, ExpenseListRequest request,
		CancellationToken cancellationToken = default) => queries.ListAsync(actor, request, cancellationToken);

	public Task<ExpenseResult<ExpenseView>> GetByIdAsync(Guid actor, Guid expenseId, CancellationToken cancellationToken = default) =>
		queries.GetByIdAsync(actor, expenseId, cancellationToken);

	public async Task<ExpenseResult<ExpenseView>> CreateAsync(Guid actor, CreateExpenseRequest request, CancellationToken cancellationToken = default)
	{
		ExpensePreview ready;
		ReceiptRecord? uploaded = null;
		try
		{
			var preparation = await PrepareCreatePreviewAsync(actor, request, cancellationToken);
			if (preparation.Value is not { } prepared)
				return transactions.Reject(Fail<ExpenseView, ExpensePreview>(preparation));
			ready = prepared;
			if (request.Attachment is { } attachment)
			{
				var receipt = await UploadReceiptAsync(actor, attachment, cancellationToken);
				if (receipt.Value is not { } value)
					return transactions.Reject(Fail<ExpenseView, ReceiptRecord>(receipt));
				uploaded = value;
			}
		}
		catch
		{
			transactions.Reject(false);
			throw;
		}
		// Resolve labels before committing so a lookup failure cannot hide a successful write.
		var result = await ExecuteWriteAsync(token => SaveNewExpenseAsync(actor, ready, request.Shares, uploaded, token),
			cancellationToken, uploaded);
		return result.Value is { } details
			? ExpenseResult<ExpenseView>.Success(new(details, ready.Labels, true, true))
			: Fail<ExpenseView, ExpenseDetails>(result);
	}

	public Task<ExpenseResult<ExpensePreview>> PreviewAsync(Guid actor, PreviewExpenseRequest request, CancellationToken cancellationToken = default)
	{
		if (request.ExpenseId == Guid.Empty)
			return Task.FromResult(Invalid<ExpensePreview>("ExpenseId", "A nonempty ID is required."));
		if (request.ExpenseId.HasValue)
			return PreviewEditAsync(actor, request, cancellationToken);
		if (request.Version is not null)
			return Task.FromResult(Invalid<ExpensePreview>("Version", "Version must be omitted/null when previewing creation."));
		return PrepareCreatePreviewAsync(actor, new CreateExpenseRequest
		{
			GroupId = request.GroupId,
			PayerId = request.PayerId,
			Name = request.Name,
			Merchant = request.Merchant,
			Amount = request.Amount,
			TransactionTime = request.TransactionTime,
			Items = request.Items,
			Shares = request.Shares,
			BankTransactionId = request.BankTransactionId
		}, cancellationToken);
	}

	private async Task<ExpenseResult<ExpensePreview>> PrepareCreatePreviewAsync(Guid actor, CreateExpenseRequest request, CancellationToken cancellationToken)
	{
		var result = await PrepareCreateAsync(actor, request, cancellationToken);
		if (result.Value is not { } prepared) return Fail<ExpensePreview, PreparedExpense>(result);
		var group = await groups.GetAsync(prepared.GroupId, cancellationToken);
		if (group.Status == 404) return ExpenseResult<ExpensePreview>.Failure(ExpenseErrorCode.NotFound);
		if (group.Status != 200 || group.Value is not { } metadata || metadata.Id != prepared.GroupId)
			return ExpenseResult<ExpensePreview>.Failure(ExpenseErrorCode.TemporarilyUnavailable);
		if (metadata.IsArchived) return ExpenseResult<ExpensePreview>.Failure(ExpenseErrorCode.NotFound);
		if (metadata.CurrencyCode != "PLN") return Invalid<ExpensePreview>("GroupId", "Only groups in PLN are supported.");
		var labels = await queries.GetLabelsAsync(metadata.Name, prepared.PayerId, prepared.Shares, prepared.Items.Select(item => item.CategoryId), cancellationToken);
		return labels is null ? ExpenseResult<ExpensePreview>.Failure(ExpenseErrorCode.TemporarilyUnavailable)
			: ExpenseResult<ExpensePreview>.Success(new(prepared, labels, request.BankTransactionId, null));
	}

	private static ExpenseResult<T> Invalid<T>(string field, string message) where T : notnull =>
		ExpenseResult<T>.Failure(ExpenseErrorCode.InvalidExpense, [new ValidationResult(message, [field])]);
	private static ExpenseResult<T> Fail<T, TSource>(ExpenseResult<TSource> result) where T : notnull where TSource : notnull =>
		ExpenseResult<T>.Failure(result.Error!.Value, result.ValidationErrors);

	private async Task<ExpenseResult<PreparedExpense>> PrepareCreateAsync(Guid userId,
		CreateExpenseRequest request, CancellationToken cancellationToken = default)
	{
		ValidateId(userId, nameof(userId));
		cancellationToken.ThrowIfCancellationRequested();
		ArgumentNullException.ThrowIfNull(request);
		var preparation = requests.Prepare(request);
		if (preparation.Value is not { } prepared)
			return ExpenseResult<PreparedExpense>.Failure(ExpenseErrorCode.InvalidExpense, preparation.Errors);
		var access = await ValidateParticipantsAsync(userId, request, previous: null, cancellationToken);
		if (!access.IsSuccess) return Fail<PreparedExpense, bool>(access);
		var validated = await ValidateCategoriesAsync(request, prepared, cancellationToken);
		if (!validated.IsSuccess) return validated;
		if (request.BankTransactionId is Guid bankId)
		{
			var bank = await ValidateNewBankAsync(userId, bankId, request, prepared, cancellationToken);
			if (!bank.IsSuccess) return bank;
		}
		return ExpenseResult<PreparedExpense>.Success(prepared);
	}

	private async Task<ExpenseResult<PreparedExpense>> ValidateCategoriesAsync(CreateExpenseRequest request,
		PreparedExpense prepared, CancellationToken cancellationToken)
	{
		var categoryIds = prepared.Items.Select(item => item.CategoryId).Distinct().ToArray();
		var foundCategories = await categories.GetByIdsAsync(categoryIds, cancellationToken);
		var missing = categoryIds.Except(foundCategories.Select(category => category.Id)).ToHashSet();
		var errors = request.Items.Select((item, index) => (item, index)).Where(pair => missing.Contains(pair.item.CategoryId))
			.Select(pair => new ValidationResult("Category does not exist.", [$"Items[{pair.index}].CategoryId"])).ToArray();
		if (errors.Length > 0)
			return ExpenseResult<PreparedExpense>.Failure(ExpenseErrorCode.InvalidExpense, errors);
		if (missing.Contains(ExpenseSystemCategories.UnknownId))
			return ExpenseResult<PreparedExpense>.Failure(ExpenseErrorCode.TemporarilyUnavailable);
		return ExpenseResult<PreparedExpense>.Success(prepared);
	}

	private async Task<ExpenseResult<PreparedExpense>> ValidateNewBankAsync(Guid userId, Guid bankId,
		CreateExpenseRequest request, PreparedExpense prepared, CancellationToken cancellationToken)
	{
		if (request.Amount is null) return Invalid<PreparedExpense>("Amount", "Amount is required when a bank transaction is selected.");
		var source = await banking.GetTransactionAsync(userId, bankId, cancellationToken);
		if (!source.IsSuccess)
			return ExpenseResult<PreparedExpense>.Failure(source.Error switch
			{
				BankingErrorCode.NotFound => ExpenseErrorCode.NotFound,
				BankingErrorCode.Forbidden => ExpenseErrorCode.Forbidden,
				_ => ExpenseErrorCode.TemporarilyUnavailable
			});
		var bank = source.Value!;
		if (bank.Amount >= 0 || bank.Status != "Booked" || !string.Equals(bank.CurrencyCode, "PLN", StringComparison.OrdinalIgnoreCase))
			return Invalid<PreparedExpense>("BankTransactionId", "Select a booked debit transaction in PLN.");
		if (prepared.PayerId != userId) return Invalid<PreparedExpense>("PayerId", "Payer must be the bank transaction owner.");
		if (prepared.Amount != -bank.Amount) return Invalid<PreparedExpense>("Amount", "Amount must equal the absolute bank transaction amount.");
		if (await bankAssignments.GetAsync(bankId, cancellationToken) is not null)
			return ExpenseResult<PreparedExpense>.Failure(ExpenseErrorCode.Conflict);
		return ExpenseResult<PreparedExpense>.Success(prepared);
	}

	private async Task<ExpenseResult<bool>> ValidateParticipantsAsync(Guid actor, CreateExpenseRequest request,
		ExpenseDetails? previous, CancellationToken token)
	{
		var existingUsers = previous?.Shares.Select(share => share.UserId).ToHashSet();
		var candidates = new List<(Guid UserId, string Field)>();
		if (request.PayerId != previous?.PayerId)
			candidates.Add((request.PayerId, "PayerId"));
		for (var index = 0; index < request.Shares.Count; index++)
		{
			var userId = request.Shares[index].UserId;
			if (existingUsers?.Contains(userId) != true)
				candidates.Add((userId, $"Shares[{index}].UserId"));
		}

		if (candidates.Count == 0) return ExpenseResult<bool>.Success(true);
		var userIds = candidates.Select(candidate => candidate.UserId).Distinct().ToArray();
		var result = await members.GetActiveMemberIdsAsync(actor, request.GroupId, userIds, token);
		if (result.Status == 404) return ExpenseResult<bool>.Failure(ExpenseErrorCode.NotFound);
		if (result.Status != 200 || result.Value is not { } active || active.Except(userIds).Any())
			return ExpenseResult<bool>.Failure(ExpenseErrorCode.TemporarilyUnavailable);
		foreach (var (userId, field) in candidates)
			if (!active.Contains(userId))
				return Invalid<bool>(field, "Payer and participants must be current members of the group.");
		return ExpenseResult<bool>.Success(true);
	}

	private static void ValidateId(Guid value, string name)
	{
		if (value == Guid.Empty) throw new ArgumentException("A nonempty ID is required.", name);
	}
}
