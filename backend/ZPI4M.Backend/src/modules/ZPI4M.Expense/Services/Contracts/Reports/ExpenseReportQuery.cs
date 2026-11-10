using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.Services.Contracts.Reports;

public sealed class ExpenseReportQuery : PagedQuery
{
	public ExpenseReportQuery() => Descending = true;
	public ExpenseReportFilter Filter { get; init; } = new();
	public ExpenseReportSortBy SortBy { get; init; } = ExpenseReportSortBy.Date;
}
