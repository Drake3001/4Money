namespace ZPI4M.Expense.Services.Contracts.Reports;

public sealed class ExpenseTimelineQuery
{
	public ExpenseReportFilter Filter { get; init; } = new();
	public ExpenseReportPeriod Period { get; init; } = ExpenseReportPeriod.Month;
	public bool ByCategory { get; init; }
}
