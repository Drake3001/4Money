using ZPI4M.Grouping.DTOs.Suggestions;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Grouping.Services.Settlements;
namespace ZPI4M.Grouping.Services.Suggestions;

public sealed class GroupSettlementSuggestionService(IGroupRepository groups, SettlementSuggestionCalculator calculator, TimeProvider clock) : IGroupSettlementSuggestionService
{
	public async Task<GroupingResult<GroupSettlementSuggestionsResponse>> GetAsync(Guid actor, Guid groupId, CancellationToken ct = default)
	{
		var snapshot = await groups.GetBalanceSnapshotAsync(actor, groupId, ct);
		if (snapshot is null) return new(404);
		try { return new(200, calculator.Calculate(snapshot, clock.GetUtcNow(), ct)); }
		catch (InconsistentGroupBalancesException) { return new(409); }
	}
}
