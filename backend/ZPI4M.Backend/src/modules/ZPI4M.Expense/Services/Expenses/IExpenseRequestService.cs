using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Services.Contracts;

namespace ZPI4M.Expense.Services.Expenses;

public interface IExpenseRequestService
{
	ExpensePreparationResult Prepare(CreateExpenseRequest request, IReadOnlySet<Guid>? editableItemIds = null);
}
