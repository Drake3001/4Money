using ZPI4M.Grouping.Services.Aggregates;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Results;

namespace ZPI4M.Grouping.Services.Expenses;

public interface IGroupExpenseContributionService
{
	Task<GroupingResult<GroupExpenseMetadata>> ApplyAsync(Guid actor, ExpenseContribution contribution,
		string expectedCurrency, CancellationToken cancellationToken = default);
	Task<GroupingResult<GroupExpenseMetadata>> ApplyChangeAsync(Guid actor, ExpenseContribution previous,
		ExpenseContribution? current, string expectedCurrency, CancellationToken cancellationToken = default);
}
