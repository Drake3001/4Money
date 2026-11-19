using ZPI4M.Expense.Services.Contracts;

namespace ZPI4M.Expense.Services.Expenses;

/// <summary>Runs sequentially after persistence, before the source transaction commits.</summary>
public interface IExpenseChangeHandler
{
	Task HandleAsync(Guid actor, ExpenseDetails? previous, ExpenseDetails? current, CancellationToken cancellationToken = default);
}
