using System.ComponentModel.DataAnnotations;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Expense.DTOs.Banking;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Services.Expenses;

public sealed partial class ExpenseService
{
	private async Task<ExpenseErrorCode?> ChangeBankAssignmentAsync(Guid expenseId, Guid? previous, Guid? next, CancellationToken token)
	{
		if (previous == next) return null;

		await transactions.EnlistAsync(bankingDb, token);
		if (next is Guid nextId)
		{
			var assigned = await bankAssignments.AssignAsync(nextId, BankTransactionConsumerType.Expense, expenseId, token);
			if (!assigned.IsSuccess) return MapBankAssignmentError(assigned.Error);
		}
		if (previous is Guid previousId)
		{
			var released = await bankAssignments.ReleaseAsync(previousId, BankTransactionConsumerType.Expense, expenseId, token);
			if (!released.IsSuccess) return MapBankAssignmentError(released.Error);
		}
		return null;
	}

	private static ExpenseErrorCode MapBankAssignmentError(BankingErrorCode? error) => error switch
	{
		BankingErrorCode.NotFound => ExpenseErrorCode.NotFound,
		BankingErrorCode.Conflict => ExpenseErrorCode.Conflict,
		_ => ExpenseErrorCode.TemporarilyUnavailable
	};

	public async Task<ExpenseResult<PagedResult<BankTransactionDetails>>> GetAvailableBankTransactionsAsync(Guid actor,
		AvailableBankTransactionRequest request, CancellationToken cancellationToken = default)
	{
		var errors = new List<ValidationResult>();
		Validator.TryValidateObject(request, new ValidationContext(request), errors, true);
		if (request.BankAccountId == Guid.Empty) errors.Add(new("A nonempty account ID is required.", ["BankAccountId"]));
		decimal? min = null, max = null;
		if (request.MinAmount is not null)
		{
			if (ExpenseRequestService.TryParseDecimal(request.MinAmount, 2, out var value)) min = value;
			else errors.Add(new("Use decimal text fitting numeric(18,2).", ["MinAmount"]));
		}
		if (request.MaxAmount is not null)
		{
			if (ExpenseRequestService.TryParseDecimal(request.MaxAmount, 2, out var value)) max = value;
			else errors.Add(new("Use decimal text fitting numeric(18,2).", ["MaxAmount"]));
		}
		if (request.DateFrom > request.DateTo) errors.Add(new("DateFrom must not exceed DateTo.", ["DateFrom", "DateTo"]));
		if (min > max) errors.Add(new("MinAmount must not exceed MaxAmount.", ["MinAmount", "MaxAmount"]));
		if (!Enum.TryParse<BankTransactionReadSortBy>(request.SortBy, true, out var sort) || !Enum.IsDefined(sort))
			errors.Add(new("Unknown sort field.", ["SortBy"]));
		if (errors.Count > 0) return ExpenseResult<PagedResult<BankTransactionDetails>>.Failure(ExpenseErrorCode.InvalidExpense, errors);
		var result = await banking.GetTransactionsAsync(actor, new BankTransactionReadQuery
		{
			BankAccountId = request.BankAccountId,
			DateFrom = request.DateFrom,
			DateTo = request.DateTo,
			MinAmount = min,
			MaxAmount = max,
			Description = request.Description,
			CurrencyCode = "PLN",
			Status = "Booked",
			OnlyDebits = true,
			ExpenseAssignment = false,
			SettlementAssignment = false,
			SortBy = sort,
			Descending = request.Descending,
			PageNumber = request.PageNumber,
			PageSize = request.PageSize
		}, cancellationToken);
		return result.Value is { } page ? ExpenseResult<PagedResult<BankTransactionDetails>>.Success(page)
			: ExpenseResult<PagedResult<BankTransactionDetails>>.Failure(result.Error switch
			{
				BankingErrorCode.NotFound => ExpenseErrorCode.NotFound,
				BankingErrorCode.Forbidden => ExpenseErrorCode.Forbidden,
				_ => ExpenseErrorCode.TemporarilyUnavailable
			});
	}
}
