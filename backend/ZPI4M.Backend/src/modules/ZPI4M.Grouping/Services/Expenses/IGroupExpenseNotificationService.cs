using ZPI4M.Grouping.Services.ExpenseMetadata;

namespace ZPI4M.Grouping.Services.Expenses;

/// <summary>Called after the expense is saved and its group is authorized and locked in the active transaction.</summary>
public interface IGroupExpenseNotificationService
{
	Task NotifyAddedAsync(Guid actor, Guid expenseId, string expenseName, decimal amount,
		GroupExpenseMetadata group, CancellationToken cancellationToken = default);
}
