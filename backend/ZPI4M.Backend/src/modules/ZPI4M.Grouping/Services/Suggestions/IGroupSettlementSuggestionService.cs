using ZPI4M.Grouping.DTOs.Suggestions;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Grouping.Services.Settlements;
namespace ZPI4M.Grouping.Services.Suggestions;

public interface IGroupSettlementSuggestionService
{
	Task<GroupingResult<GroupSettlementSuggestionsResponse>> GetAsync(Guid actor, Guid groupId, CancellationToken ct = default);
}
