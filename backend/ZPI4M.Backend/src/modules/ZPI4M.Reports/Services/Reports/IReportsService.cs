using ZPI4M.Reports.DTOs.CategoryBreakdown;
using ZPI4M.Reports.DTOs.DailyTotals;
using ZPI4M.Reports.DTOs.DayOfWeekTotals;
using ZPI4M.Reports.DTOs.PeriodCategoryTotals;

namespace ZPI4M.Reports.Services.Reports;

/// <summary>Actor comes from ICurrentUser at the authenticated boundary, never from report parameters.</summary>
public interface IReportsService
{
	Task<CategoryBreakdownResponse> GetCategoryBreakdownAsync(Guid actor, CategoryBreakdownRequest request,
		CancellationToken cancellationToken = default);

	Task<ExpenseTotalsByPeriodAndCategoryResponse> GetExpenseTotalsByPeriodAndCategoryAsync(
		Guid actor, ExpenseTotalsByPeriodAndCategoryRequest request, CancellationToken cancellationToken = default);

	Task<ExpenseTotalsByDayOfWeekResponse> GetExpenseTotalsByDayOfWeekAsync(
		Guid actor, ExpenseTotalsByDayOfWeekRequest request, CancellationToken cancellationToken = default);

	Task<DailyTotalsResponse> GetDailyExpenseTotalsAsync(Guid actor, DailyTotalsRequest request,
		CancellationToken cancellationToken = default);
}
