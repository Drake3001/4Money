using Xunit;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Services.Calendar;

namespace ZPI4M.Limits.UnitTests;

public sealed class LimitOverlapPolicyTests
{
	[Theory]
	[InlineData(LimitPeriod.Custom, LimitPeriod.Custom, true)]
	[InlineData(LimitPeriod.Custom, LimitPeriod.Week, true)]
	[InlineData(LimitPeriod.Custom, LimitPeriod.Month, true)]
	[InlineData(LimitPeriod.Custom, LimitPeriod.Year, true)]
	[InlineData(LimitPeriod.Week, LimitPeriod.Custom, true)]
	[InlineData(LimitPeriod.Month, LimitPeriod.Custom, true)]
	[InlineData(LimitPeriod.Year, LimitPeriod.Custom, true)]
	[InlineData(LimitPeriod.Month, LimitPeriod.Month, false)]
	[InlineData(LimitPeriod.Week, LimitPeriod.Month, false)]
	[InlineData(LimitPeriod.Year, LimitPeriod.Month, false)]
	public void OnlyEnabledCalendarsInTheSameScopeConflict(LimitPeriod proposedPeriod, LimitPeriod existingPeriod, bool allowed)
	{
		var existing = new UserLimit
		{
			Id = Guid.NewGuid(),
			Period = existingPeriod,
			IsEnabled = true,
			StartDate = existingPeriod == LimitPeriod.Custom ? new(2026, 1, 1) : null,
			EndDate = existingPeriod == LimitPeriod.Custom ? new(2026, 1, 2) : null
		};
		var version = existing.Version;
		Assert.Equal(allowed, LimitOverlapPolicy.CanEnable(new UserLimit { Id = Guid.NewGuid(), Period = proposedPeriod }, [existing]));
		Assert.True(existing.IsEnabled);
		Assert.Equal(version, existing.Version);
		Assert.True(LimitOverlapPolicy.CanEnable(existing, [existing]));
		Assert.True(LimitOverlapPolicy.CanEnable(new UserLimit { Period = proposedPeriod }, []));
	}
}
