using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Services.Calendar;

public static class LimitOverlapPolicy
{
	// The caller holds the owner/group lock; definitions are scoped to owner/category/currency.
	public static bool CanEnable(ILimitDefinition proposed, IEnumerable<ILimitDefinition> enabled) =>
		proposed.Period == LimitPeriod.Custom || !enabled.Any(l => l.Id != proposed.Id && l.Period != LimitPeriod.Custom);
}
