namespace ZPI4M.Expense.Services.Contracts.Reports;

/// <summary>Both dates are inclusive. UserId selects whose costs are calculated; it does not grant access.</summary>
public sealed class ExpenseReportFilter
{
	public Guid? UserId { get; init; }
	public Guid? GroupId { get; init; }
	public Guid? ParticipantId { get; init; }
	public Guid? PayerId { get; init; }
	public DateOnly? DateFrom { get; init; }
	public DateOnly? DateTo { get; init; }
	public decimal? MinAmount { get; init; }
	public decimal? MaxAmount { get; init; }
	public Guid? CategoryId { get; init; }
	public string? Search { get; init; }
	public bool? HasReceipt { get; init; }
	public bool? HasBankTransaction { get; init; }
}
