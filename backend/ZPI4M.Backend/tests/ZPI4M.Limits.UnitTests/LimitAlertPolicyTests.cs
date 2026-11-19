using Xunit;
using ZPI4M.Limits.Services.Alerts;
using ZPI4M.Notifications.Models;

namespace ZPI4M.Limits.UnitTests;

public sealed class LimitAlertPolicyTests
{
	[Theory]
	[InlineData("79.9999", false, null)]
	[InlineData("80", false, NotificationType.LimitApproaching)]
	[InlineData("100", false, NotificationType.LimitApproaching)]
	[InlineData("100", true, null)]
	[InlineData("100.0001", false, NotificationType.LimitExceeded)]
	[InlineData("100.0001", true, NotificationType.LimitExceeded)]
	public void ExactAmountsDetermineThresholdBeforeDisplayRounding(string spent, bool handled, NotificationType? expected) =>
		Assert.Equal(expected, LimitAlertService.SelectType(decimal.Parse(spent, System.Globalization.CultureInfo.InvariantCulture), 100, 80, handled));

	[Theory]
	[InlineData(-1, false)]
	[InlineData(0, false)]
	[InlineData(1, true)]
	[InlineData(80, true)]
	[InlineData(99, true)]
	[InlineData(100, false)]
	[InlineData(101, false)]
	public void ThresholdConfigurationMustBeStrictlyBetweenZeroAndOneHundred(int threshold, bool valid) =>
		Assert.Equal(valid, new LimitsAlertsOptionsValidator().Validate(null, new() { WarningThresholdPercent = threshold }).Succeeded);
}
