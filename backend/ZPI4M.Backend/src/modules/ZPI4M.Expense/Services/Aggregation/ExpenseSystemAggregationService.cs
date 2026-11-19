using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Grouping.Services.Transactions;

namespace ZPI4M.Expense.Services.Aggregation;

public sealed class ExpenseSystemAggregationService(IExpenseRepository expenses,
	IGroupingTransactionCoordinator transactions) : IExpenseSystemAggregationService
{
	public async Task<ExpenseResult<ExpenseUsageTotal>> GetGroupUsageTotalAsync(Guid groupId, DateOnly dateFrom,
		DateOnly dateTo, Guid? categoryId, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		transactions.EnsureActive();
		if (groupId == Guid.Empty || categoryId == Guid.Empty || dateFrom > dateTo)
			return ExpenseResult<ExpenseUsageTotal>.Failure(ExpenseErrorCode.InvalidExpense);
		// A trusted group context grants only this group's rows, never unrelated data.
		var amount = await expenses.GetUsageTotalAsync(Guid.Empty, [groupId], dateFrom, dateTo,
			ExpenseAggregationScope.Group, groupId, categoryId, cancellationToken);
		return ExpenseResult<ExpenseUsageTotal>.Success(new(amount));
	}
}
