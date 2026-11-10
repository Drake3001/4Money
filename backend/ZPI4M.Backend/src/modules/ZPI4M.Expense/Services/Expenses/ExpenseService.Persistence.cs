using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Reads;
using ZPI4M.Grouping.Services.Aggregates;
using ReceiptRecord = ZPI4M.Receipt.Models.Receipt;

namespace ZPI4M.Expense.Services.Expenses;

public sealed partial class ExpenseService
{
	private async Task<ExpenseResult<ExpenseDetails>> SaveNewExpenseAsync(Guid actor, ExpensePreview ready,
		IReadOnlyList<CreateExpenseShareRequest> inputShares, ReceiptRecord? uploaded, CancellationToken cancellationToken)
	{
		var prepared = ready.Expense;
		var contribution = Contribution(prepared, inputShares);
		var applied = await groupExpenses.ApplyAsync(actor, contribution, "PLN", cancellationToken);
		if (applied.Status != 200 || applied.Value is not { } group)
			return ExpenseResult<ExpenseDetails>.Failure(MapGroupingError(applied.Status),
				applied.Errors?.SelectMany(pair => pair.Value.Select(message => new ValidationResult(message, [pair.Key]))).ToArray());
		if (uploaded is not null)
		{
			await transactions.EnlistAsync(receiptDb, cancellationToken);
			await receipts.InsertAsync(uploaded, cancellationToken);
		}
		var expense = new Models.Expense
		{
			Id = Guid.NewGuid(),
			FkGroupId = prepared.GroupId,
			FkPayerId = prepared.PayerId,
			Name = prepared.Name,
			Merchant = prepared.Merchant,
			Amount = prepared.Amount,
			TransactionTime = prepared.TransactionTime,
			FkBankTransactionId = ready.BankTransactionId,
			FkReceiptId = uploaded?.Id
		};
		if (await ChangeBankAssignmentAsync(expense.Id, null, ready.BankTransactionId, cancellationToken) is { } bankError)
			return ExpenseResult<ExpenseDetails>.Failure(bankError);

		expenses.Add(expense);
		var expenseItems = prepared.Items.Select(item => new ExpenseItem
		{
			Id = Guid.NewGuid(),
			FkExpenseId = expense.Id,
			FkCategoryId = item.CategoryId,
			Name = item.Name,
			UnitPrice = item.UnitPrice,
			Quantity = item.Quantity,
			Discount = item.Discount,
			Unit = item.Unit,
			IsSystemGenerated = item.IsSystemGenerated
		}).ToArray();
		items.AddRange(expenseItems);
		foreach (var share in prepared.Shares)
			shares.Add(new ExpenseShare { FkExpenseId = expense.Id, FkUserId = share.UserId, Percentage = share.Percentage });
		var details = ExpenseQueryService.MapDetails(expense, expenseItems, prepared.Shares);
		await db.SaveChangesAsync(cancellationToken);
		await groupNotifications.NotifyAddedAsync(actor, expense.Id, expense.Name, expense.Amount, group, cancellationToken);
		return ExpenseResult<ExpenseDetails>.Success(details);
	}

	private async Task<ExpenseResult<T>> ExecuteWriteAsync<T>(Func<CancellationToken, Task<ExpenseResult<T>>> action,
		CancellationToken cancellationToken, ReceiptRecord? uploaded = null) where T : notnull
	{
		var cleanupRegistered = false;
		try
		{
			return await transactions.ExecuteAsync(async token =>
			{
				if (uploaded is not null)
				{
					transactions.AfterRollback(() => receipts.DeleteFileAsync(uploaded));
					cleanupRegistered = true;
				}
				await transactions.EnlistAsync(db, token);
				return await action(token);
			}, result => result.IsSuccess, cancellationToken);
		}
		catch (DbUpdateConcurrencyException)
		{
			return ExpenseResult<T>.Failure(ExpenseErrorCode.Conflict);
		}
		catch (DbUpdateException error) when (error.InnerException is PostgresException
		{
			SqlState: PostgresErrorCodes.UniqueViolation,
			ConstraintName: "c_expense_unique_fk_bank_transaction_id" or "c_expense_unique_fk_receipt_id"
		})
		{
			return ExpenseResult<T>.Failure(ExpenseErrorCode.Conflict);
		}
		finally
		{
			// A failure to start the transaction cannot have persisted the new receipt.
			if (uploaded is not null && !cleanupRegistered)
				await receipts.DeleteFileAsync(uploaded);
		}
	}

	private async Task<IReadOnlyList<ExpenseItem>> UpdateItemsAsync(Guid expenseId, IReadOnlyList<PreparedExpenseItem> prepared,
		CancellationToken token)
	{
		var existing = (await items.GetTrackedByExpenseAsync(expenseId, token)).ToDictionary(item => item.Id);
		var result = new List<ExpenseItem>();
		foreach (var value in prepared)
		{
			ExpenseItem item;
			if (value.Id is Guid id)
			{
				item = existing[id];
				existing.Remove(id);
			}
			else
			{
				item = new() { Id = Guid.NewGuid(), FkExpenseId = expenseId };
				items.Add(item);
			}
			item.FkCategoryId = value.CategoryId;
			item.Name = value.Name;
			item.UnitPrice = value.UnitPrice;
			item.Quantity = value.Quantity;
			item.Discount = value.Discount;
			item.Unit = value.Unit;
			item.IsSystemGenerated = value.IsSystemGenerated;
			result.Add(item);
		}
		items.RemoveRange(existing.Values);
		return result;
	}

	private async Task UpdateSharesAsync(Guid expenseId, IReadOnlyList<ExpenseShareDetails> prepared, CancellationToken token)
	{
		var existing = (await shares.GetTrackedByExpenseAsync(expenseId, token)).ToDictionary(share => share.FkUserId);
		foreach (var value in prepared)
		{
			if (!existing.Remove(value.UserId, out var share))
			{
				share = new() { FkExpenseId = expenseId, FkUserId = value.UserId };
				shares.Add(share);
			}
			share.Percentage = value.Percentage;
		}
		shares.RemoveRange(existing.Values);
	}

	private static ExpenseContribution Contribution(PreparedExpense expense, IReadOnlyList<CreateExpenseShareRequest> inputShares)
	{
		var amounts = expense.Shares.ToDictionary(share => share.UserId, share => share.Amount);
		// Grouping reports share validation errors using the contribution's input order.
		var orderedShares = inputShares.Select(share => new ExpenseShareContribution(share.UserId, amounts[share.UserId])).ToArray();
		return new(expense.GroupId, expense.PayerId, expense.Amount, orderedShares);
	}

	private static ExpenseContribution Contribution(Guid groupId, Guid payerId, decimal amount, IReadOnlyList<ExpenseShareDetails> shares) =>
		new(groupId, payerId, amount, shares.Select(share => new ExpenseShareContribution(share.UserId, share.Amount)).ToArray());

	private static ExpenseErrorCode MapGroupingError(int status) => status switch
	{
		400 => ExpenseErrorCode.InvalidExpense,
		403 => ExpenseErrorCode.Forbidden,
		404 => ExpenseErrorCode.NotFound,
		409 => ExpenseErrorCode.Conflict,
		_ => ExpenseErrorCode.TemporarilyUnavailable
	};
}
