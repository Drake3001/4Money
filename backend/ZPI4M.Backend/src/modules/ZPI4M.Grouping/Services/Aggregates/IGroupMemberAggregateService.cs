namespace ZPI4M.Grouping.Services.Aggregates;

public interface IGroupMemberAggregateService
{
	Task ApplySettlementChangeAsync(SettlementContribution? previous, SettlementContribution? current, CancellationToken ct = default);
	Task ApplyExpenseChangeAsync(ExpenseContribution? previous, ExpenseContribution? current, CancellationToken ct = default);
}
