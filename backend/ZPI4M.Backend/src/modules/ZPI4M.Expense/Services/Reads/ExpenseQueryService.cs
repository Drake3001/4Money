using System.ComponentModel.DataAnnotations;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseItemRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Repositories.ExpenseShareRepository;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Expense.Services.Reads;

public sealed class ExpenseQueryService(IExpenseRepository expenses, IExpenseItemRepository items,
	IExpenseShareRepository shares, ICategoryRepository categories, IGroupMemberService members,
	IGroupExpenseMetadataLookup groups, IUserDisplayNameLookup users,
	IExpenseCalculationService calculations)
{
	public async Task<ExpenseResult<IReadOnlyList<CategoryDetails>>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
		ExpenseResult<IReadOnlyList<CategoryDetails>>.Success((await categories.GetAllAsync(cancellationToken)).Select(MapCategory).ToArray());

	public async Task<ExpenseResult<ExpenseView>> GetByIdAsync(Guid actor, Guid expenseId, CancellationToken cancellationToken = default)
	{
		var result = await ReadAccessAsync(actor, expenseId, cancellationToken);
		if (result.Value is not { } access) return ExpenseResult<ExpenseView>.Failure(result.Error!.Value, result.ValidationErrors);
		var details = access.Expense;
		var labels = await GetLabelsAsync(access.GroupName, details.PayerId, details.Shares,
			details.Items.Select(item => item.CategoryId), cancellationToken);
		return labels is null ? ExpenseResult<ExpenseView>.Failure(ExpenseErrorCode.TemporarilyUnavailable)
			: ExpenseResult<ExpenseView>.Success(new(details, labels, access.CanWrite, access.CanWrite));
	}

	internal sealed record ExpenseAccess(ExpenseDetails Expense, string GroupName, bool CanWrite);

	internal async Task<ExpenseResult<ExpenseAccess>> ReadAccessAsync(Guid actor, Guid expenseId, CancellationToken cancellationToken)
	{
		if (expenseId == Guid.Empty) return ExpenseResult<ExpenseAccess>.Failure(ExpenseErrorCode.InvalidExpense,
			[new ValidationResult("A nonempty ID is required.", ["ExpenseId"])]);
		var result = await ReadDetailsAsync(actor, expenseId, cancellationToken);
		if (result.Value is not { } details) return ExpenseResult<ExpenseAccess>.Failure(result.Error!.Value, result.ValidationErrors);
		// Internal metadata lookup follows Expense authorization; the public Grouping GET
		// intentionally denies former members and cannot supply historical Expense labels.
		var group = await groups.GetAsync(details.GroupId, cancellationToken);
		if (group.Status != 200 || group.Value is not { } metadata || metadata.Id != details.GroupId || metadata.CurrencyCode != "PLN")
			return ExpenseResult<ExpenseAccess>.Failure(ExpenseErrorCode.TemporarilyUnavailable);
		var member = await members.GetAsync(actor, details.GroupId, actor, cancellationToken);
		if (member.Status != 404 && (member.Status != 200 || member.Value is null || member.Value.UserId != actor || member.Value.GroupId != details.GroupId))
			return ExpenseResult<ExpenseAccess>.Failure(ExpenseErrorCode.TemporarilyUnavailable);
		var canWrite = member.Status == 200 && member.Value!.IsMember && !metadata.IsArchived;
		return ExpenseResult<ExpenseAccess>.Success(new(details, metadata.Name, canWrite));
	}

	internal async Task<ExpenseLabels?> GetLabelsAsync(string groupName, Guid payer, IReadOnlyList<ExpenseShareDetails> shares,
		IEnumerable<Guid> categoryIds, CancellationToken cancellationToken)
	{
		var ids = shares.Select(share => share.UserId).Append(payer).Distinct().ToArray();
		var names = await users.GetAsync(ids, cancellationToken);
		var requestedCategories = categoryIds.Distinct().ToArray();
		var found = (await categories.GetByIdsAsync(requestedCategories, cancellationToken)).Select(MapCategory).ToDictionary(category => category.Id);
		return ids.Any(id => !names.ContainsKey(id)) || requestedCategories.Any(id => !found.ContainsKey(id))
			? null : new(groupName, names, found);
	}

	private static CategoryDetails MapCategory(Category category) => new(category.Id, category.Name, category.Description,
		category.Icon, category.Id == ExpenseSystemCategories.UnknownId);
	public async Task<ExpenseResult<PagedResult<ExpenseHistoryEntry>>> ListAsync(Guid actor, ExpenseListRequest request,
		CancellationToken cancellationToken = default)
	{
		var errors = new List<ValidationResult>();
		Validator.TryValidateObject(request, new ValidationContext(request), errors, true);
		if (request.GroupId == Guid.Empty || request.ParticipantId == Guid.Empty ||
			request.PayerId == Guid.Empty || request.CategoryId == Guid.Empty)
			errors.Add(new("Filter IDs must be nonempty."));
		if (request.TransactionTimeFrom >= request.TransactionTimeTo)
			errors.Add(new("Use an increasing date range.", [nameof(request.TransactionTimeFrom), nameof(request.TransactionTimeTo)]));
		var min = ReadAmountFilter(request.MinAmount, nameof(request.MinAmount), errors);
		var max = ReadAmountFilter(request.MaxAmount, nameof(request.MaxAmount), errors);
		if (min > max) errors.Add(new("MinAmount must not exceed MaxAmount.", [nameof(request.MinAmount)]));
		var sort = request.SortBy?.ToLowerInvariant();
		if (sort is not ("transactiontime" or "amount" or "name"))
			errors.Add(new("Unknown sort field.", [nameof(request.SortBy)]));
		if (errors.Count != 0)
			return ExpenseResult<PagedResult<ExpenseHistoryEntry>>.Failure(ExpenseErrorCode.InvalidExpense, errors);

		var activeGroups = await groups.GetActiveGroupIdsAsync(actor, cancellationToken);
		if (request.GroupId is Guid groupId && !activeGroups.Contains(groupId) &&
			!await expenses.HasRelatedExpenseInGroupAsync(actor, groupId, cancellationToken))
			return ExpenseResult<PagedResult<ExpenseHistoryEntry>>.Failure(ExpenseErrorCode.NotFound);
		var page = await expenses.GetHistoryAsync(actor, activeGroups, new ExpenseHistoryQuery
		{
			GroupId = request.GroupId,
			ParticipantId = request.ParticipantId ?? (request.GroupId is null ? actor : (Guid?)null),
			PayerId = request.PayerId,
			TransactionTimeFrom = request.TransactionTimeFrom,
			TransactionTimeTo = request.TransactionTimeTo,
			MinAmount = min,
			MaxAmount = max,
			CategoryId = request.CategoryId,
			HasReceipt = request.HasReceipt,
			HasBankTransaction = request.HasBankTransaction,
			Search = request.Search,
			SortBy = sort switch { "amount" => ExpenseSortBy.Amount, "name" => ExpenseSortBy.Name, _ => ExpenseSortBy.TransactionTime },
			Descending = request.Descending,
			PageNumber = request.PageNumber,
			PageSize = request.PageSize
		}, cancellationToken);
		if (page.Items.Count == 0)
			return ExpenseResult<PagedResult<ExpenseHistoryEntry>>.Success(new([], page.TotalCount, request.PageNumber, request.PageSize));

		var ids = page.Items.Select(expense => expense.Id).ToArray();
		var shareRows = (await shares.GetByExpensesAsync(ids, cancellationToken)).ToLookup(share => share.FkExpenseId);
		var payerNames = await users.GetAsync(page.Items.Select(expense => expense.FkPayerId).Distinct().ToArray(), cancellationToken);
		var groupNames = new Dictionary<Guid, string>();
		foreach (var id in page.Items.Select(expense => expense.FkGroupId).Distinct())
		{
			var group = await groups.GetAsync(id, cancellationToken);
			if (group.Status != 200 || group.Value is null)
				return ExpenseResult<PagedResult<ExpenseHistoryEntry>>.Failure(ExpenseErrorCode.TemporarilyUnavailable);
			groupNames.Add(id, group.Value.Name);
		}

		var result = new List<ExpenseHistoryEntry>();
		foreach (var expense in page.Items)
		{
			if (!shareRows.Contains(expense.Id)) continue;
			if (!payerNames.TryGetValue(expense.FkPayerId, out var payerName))
				return ExpenseResult<PagedResult<ExpenseHistoryEntry>>.Failure(ExpenseErrorCode.TemporarilyUnavailable);
			var myShare = calculations.CalculateShares(expense.Amount, shareRows[expense.Id].Select(share => (share.FkUserId, share.Percentage)))
				.FirstOrDefault(share => share.UserId == actor);
			var entryCategories = expense.ExpenseItems.Select(item => item.FkCategory).DistinctBy(category => category.Id)
				.OrderBy(category => category.Id).Select(MapCategory).ToArray();
			result.Add(new(expense, groupNames[expense.FkGroupId], payerName, myShare, entryCategories));
		}
		return ExpenseResult<PagedResult<ExpenseHistoryEntry>>.Success(new(result, page.TotalCount, request.PageNumber, request.PageSize));
	}

	private static decimal? ReadAmountFilter(string? text, string field, List<ValidationResult> errors)
	{
		if (text is null) return null;
		if (ExpenseRequestService.TryParseDecimal(text, 2, out var value) && value > 0) return value;
		errors.Add(new("Use positive decimal text fitting numeric(18,2).", [field]));
		return null;
	}

	private async Task<ExpenseResult<ExpenseDetails>> ReadDetailsAsync(Guid userId, Guid expenseId,
		CancellationToken cancellationToken = default)
	{
		if (userId == Guid.Empty) throw new ArgumentException("A nonempty ID is required.", nameof(userId));
		var expense = await expenses.GetReadAsync(expenseId, cancellationToken);
		if (expense is null) return ExpenseResult<ExpenseDetails>.Failure(ExpenseErrorCode.NotFound);
		var error = await CheckReadAccessAsync(userId, expense, cancellationToken);
		if (error.HasValue) return ExpenseResult<ExpenseDetails>.Failure(error.Value);
		var expenseItems = await items.GetAllByExpenseAsync(expenseId, cancellationToken);
		var expenseShares = await shares.GetByExpenseAsync(expenseId, cancellationToken);
		var current = await expenses.GetReadAsync(expenseId, cancellationToken);
		if (current is null) return ExpenseResult<ExpenseDetails>.Failure(ExpenseErrorCode.NotFound);
		if (current.Version != expense.Version) return ExpenseResult<ExpenseDetails>.Failure(ExpenseErrorCode.Conflict);
		var amounts = calculations.CalculateShares(expense.Amount, expenseShares.Select(share => (share.FkUserId, share.Percentage)));
		return ExpenseResult<ExpenseDetails>.Success(MapDetails(expense, expenseItems, amounts));
	}

	internal async Task<ExpenseErrorCode?> CheckReadAccessAsync(Guid userId, Models.Expense expense, CancellationToken cancellationToken)
	{
		if (expense.FkPayerId == userId || await shares.ExistsAsync(expense.Id, userId, cancellationToken)) return null;
		var member = await members.GetAsync(userId, expense.FkGroupId, userId, cancellationToken);
		if (member.Status == 404 || member.Status == 200 && member.Value is { IsMember: false }) return ExpenseErrorCode.NotFound;
		return member.Status == 200 && member.Value is { IsMember: true } && member.Value.UserId == userId && member.Value.GroupId == expense.FkGroupId
			? null : ExpenseErrorCode.TemporarilyUnavailable;
	}

	internal static ExpenseDetails MapDetails(Models.Expense expense, IReadOnlyList<ExpenseItem> expenseItems, IReadOnlyList<ExpenseShareDetails> expenseShares) => new(
		expense.Id, expense.FkGroupId, expense.FkPayerId, expense.Name, expense.Merchant, expense.Amount,
		expense.TransactionTime, expense.FkBankTransactionId, expense.FkReceiptId,
		expenseItems.OrderBy(item => item.Id).Select(item => new ExpenseItemDetails(item.Id, item.FkCategoryId, item.Name, item.UnitPrice,
			item.Quantity, item.Discount, item.Unit, ExpenseCalculationService.CalculateItemTotal(item.UnitPrice, item.Quantity, item.Discount), item.IsSystemGenerated)).ToArray(),
		expenseShares)
	{ Version = expense.Version };
}
