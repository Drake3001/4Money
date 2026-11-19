using System.ComponentModel.DataAnnotations;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Reads;
using ExpenseAccess = ZPI4M.Expense.Services.Reads.ExpenseQueryService.ExpenseAccess;
using ReceiptRecord = ZPI4M.Receipt.Models.Receipt;

namespace ZPI4M.Expense.Services.Expenses;

public sealed partial class ExpenseService
{
	private async Task<ExpenseResult<ExpensePreview>> PreviewEditAsync(Guid actor, PreviewExpenseRequest request, CancellationToken token)
	{
		var existing = await ReadEditableAsync(actor, request.ExpenseId!.Value, request.Version, token);
		if (existing.Value is not { } access) return Fail<ExpensePreview, ExpenseAccess>(existing);
		if (request.GroupId != access.Expense.GroupId)
			return Invalid<ExpensePreview>("GroupId", "GroupId must match the expense group.");
		return await PrepareEditPreviewAsync(actor, access, new CreateExpenseRequest
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
		}, token);
	}

	public async Task<ExpenseResult<ExpenseView>> UpdateAsync(Guid actor, Guid expenseId, UpdateExpenseRequest request,
		CancellationToken cancellationToken = default)
	{
		ExpenseDetails previous;
		ExpensePreview ready;
		ReceiptRecord? uploaded = null;
		try
		{
			var existing = await ReadEditableAsync(actor, expenseId, request.Version, cancellationToken);
			if (existing.Value is not { } access) return transactions.Reject(Fail<ExpenseView, ExpenseAccess>(existing));
			previous = access.Expense;
			var result = await PrepareEditPreviewAsync(actor, access, new CreateExpenseRequest
			{
				GroupId = previous.GroupId,
				PayerId = request.PayerId,
				Name = request.Name,
				Merchant = request.Merchant,
				Amount = request.Amount,
				TransactionTime = request.TransactionTime,
				Items = request.Items,
				Shares = request.Shares,
				BankTransactionId = request.BankTransactionId
			}, cancellationToken);
			if (result.Value is not { } preparedView)
				return transactions.Reject(Fail<ExpenseView, ExpensePreview>(result));
			ready = preparedView;
			if (request.Attachment is not null && request.RemoveReceipt)
				return transactions.Reject(Invalid<ExpenseView>("RemoveReceipt", "Choose either removal or a replacement file."));
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
		return await ExecuteWriteAsync(async token =>
		{
			var prepared = ready.Expense;
			var applied = await groupExpenses.ApplyChangeAsync(actor, Contribution(previous.GroupId, previous.PayerId, previous.Amount, previous.Shares),
				Contribution(prepared, request.Shares), "PLN", token);
			if (applied.Value is not { } metadata)
				return ExpenseResult<ExpenseView>.Failure(MapGroupingError(applied.Status),
					applied.Errors?.SelectMany(pair => pair.Value.Select(message =>
						new ValidationResult(message, [pair.Key]))).ToArray());

			var expense = await expenses.GetByIdAsync(expenseId, token);
			if (expense is null || expense.Version != previous.Version)
				return ExpenseResult<ExpenseView>.Failure(ExpenseErrorCode.Conflict);
			if (await ChangeBankAssignmentAsync(expenseId, previous.BankTransactionId, ready.BankTransactionId, token) is { } bankError)
				return ExpenseResult<ExpenseView>.Failure(bankError);

			expense.FkPayerId = prepared.PayerId;
			expense.Name = prepared.Name;
			expense.Merchant = prepared.Merchant;
			expense.Amount = prepared.Amount;
			expense.TransactionTime = prepared.TransactionTime;
			expense.FkBankTransactionId = ready.BankTransactionId;
			if (request.RemoveReceipt) expense.FkReceiptId = null;
			if (uploaded is not null)
			{
				await transactions.EnlistAsync(receiptDb, token);
				await receipts.InsertAsync(uploaded, token);
				expense.FkReceiptId = uploaded.Id;
			}
			expense.Version = Guid.NewGuid();

			var updatedItems = await UpdateItemsAsync(expenseId, prepared.Items, token);
			await UpdateSharesAsync(expenseId, prepared.Shares, token);
			await db.SaveChangesAsync(token);
			await HandleChangeAsync(actor, previous, ExpenseQueryService.MapDetails(expense, updatedItems, prepared.Shares), token);
			if (previous.ReceiptId is Guid receiptId && receiptId != expense.FkReceiptId)
				await RemoveReceiptAsync(receiptId, token);
			return ExpenseResult<ExpenseView>.Success(new(ExpenseQueryService.MapDetails(expense, updatedItems, prepared.Shares),
				ready.Labels with { GroupName = metadata.Name }, true, true));
		}, cancellationToken, uploaded);
	}

	public Task<ExpenseResult<bool>> DeleteAsync(Guid actor, Guid expenseId, string version,
		CancellationToken cancellationToken = default) =>
		ExecuteWriteAsync(async token =>
		{
			var existing = await ReadEditableAsync(actor, expenseId, version, token);
			if (existing.Value is not { } access) return Fail<bool, ExpenseAccess>(existing);
			var previous = access.Expense;
			var applied = await groupExpenses.ApplyChangeAsync(actor,
				Contribution(previous.GroupId, previous.PayerId, previous.Amount, previous.Shares), null, "PLN", token);
			if (applied.Status != 200) return ExpenseResult<bool>.Failure(MapGroupingError(applied.Status));

			var expense = await expenses.GetByIdAsync(expenseId, token);
			if (expense is null || expense.Version != previous.Version)
				return ExpenseResult<bool>.Failure(ExpenseErrorCode.Conflict);
			if (await ChangeBankAssignmentAsync(expenseId, previous.BankTransactionId, null, token) is { } bankError)
				return ExpenseResult<bool>.Failure(bankError);

			await items.DeleteByExpenseAsync(expenseId, token);
			await shares.DeleteByExpenseAsync(expenseId, token);
			expenses.Remove(expense);
			await db.SaveChangesAsync(token);
			await HandleChangeAsync(actor, previous, null, token);
			if (previous.ReceiptId is Guid receiptId)
				await RemoveReceiptAsync(receiptId, token);
			return ExpenseResult<bool>.Success(true);
		}, cancellationToken);

	private async Task<ExpenseResult<ExpenseAccess>> ReadEditableAsync(Guid actor, Guid expenseId, string? version, CancellationToken token)
	{
		if (string.IsNullOrWhiteSpace(version)) return Invalid<ExpenseAccess>("Version", "Version is required.");
		var existing = await queries.ReadAccessAsync(actor, expenseId, token);
		if (existing.Value is not { } access) return existing;
		if (!access.CanWrite) return ExpenseResult<ExpenseAccess>.Failure(ExpenseErrorCode.Forbidden);
		return string.Equals(version, access.Expense.Version.ToString("D"), StringComparison.Ordinal)
			? existing : ExpenseResult<ExpenseAccess>.Failure(ExpenseErrorCode.Conflict);
	}

	private async Task<ExpenseResult<ExpensePreview>> PrepareEditPreviewAsync(Guid actor, ExpenseAccess access, CreateExpenseRequest request,
		CancellationToken token)
	{
		var previous = access.Expense;
		var editableIds = previous.Items.Where(item => !item.IsSystemGenerated).Select(item => item.Id).ToHashSet();
		var result = requests.Prepare(request, editableIds);
		if (result.Value is not { } prepared)
			return ExpenseResult<ExpensePreview>.Failure(ExpenseErrorCode.InvalidExpense, result.Errors);

		if (request.BankTransactionId is Guid bankId)
		{
			if (bankId == previous.BankTransactionId)
			{
				if (prepared.PayerId != previous.PayerId)
					return Invalid<ExpensePreview>("PayerId", "Keep the payer of the attached bank transaction.");
				if (request.Amount is null || prepared.Amount != previous.Amount)
					return Invalid<ExpensePreview>("Amount", "Keep the amount of the attached bank transaction.");
			}
			else
			{
				var bank = await ValidateNewBankAsync(actor, bankId, request, prepared, token);
				if (!bank.IsSuccess) return Fail<ExpensePreview, PreparedExpense>(bank);
			}
		}

		var membership = await ValidateParticipantsAsync(actor, request, previous, token);
		if (!membership.IsSuccess) return Fail<ExpensePreview, bool>(membership);
		var validated = await ValidateCategoriesAsync(request, prepared, token);
		if (!validated.IsSuccess) return Fail<ExpensePreview, PreparedExpense>(validated);
		var labels = await queries.GetLabelsAsync(access.GroupName, prepared.PayerId, prepared.Shares,
			prepared.Items.Select(item => item.CategoryId), token);
		return labels is null ? ExpenseResult<ExpensePreview>.Failure(ExpenseErrorCode.TemporarilyUnavailable)
			: ExpenseResult<ExpensePreview>.Success(new(prepared, labels, request.BankTransactionId, previous.ReceiptId));
	}
}
