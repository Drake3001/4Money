using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Reports;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Services.Reads;

public interface IExpenseReadService
{
	Task<ExpenseResult<PagedResult<ExpenseReportEntry>>> GetExpensesAsync(Guid actor, ExpenseReportQuery query,
		CancellationToken cancellationToken = default);
	Task<ExpenseResult<ExpenseReportTotals>> GetTotalsAsync(Guid actor, ExpenseReportFilter filter,
		CancellationToken cancellationToken = default);
	Task<ExpenseResult<IReadOnlyList<ExpenseCategoryTotal>>> GetCategoryTotalsAsync(Guid actor, ExpenseReportFilter filter,
		ExpenseCategorySortBy sortBy = ExpenseCategorySortBy.Amount, bool descending = true,
		CancellationToken cancellationToken = default);
	Task<ExpenseResult<IReadOnlyList<ExpensePeriodTotal>>> GetTimelineAsync(Guid actor, ExpenseTimelineQuery query,
		CancellationToken cancellationToken = default);
}
