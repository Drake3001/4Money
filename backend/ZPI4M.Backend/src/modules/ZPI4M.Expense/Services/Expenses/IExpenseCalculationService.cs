using ZPI4M.Expense.Services.Contracts;

namespace ZPI4M.Expense.Services.Expenses;

public interface IExpenseCalculationService
{
	IReadOnlyList<ExpenseShareDetails> CalculateShares(decimal amount, IEnumerable<(Guid UserId, decimal Percentage)> shares);
}
