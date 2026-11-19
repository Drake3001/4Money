using Microsoft.Extensions.Options;

namespace ZPI4M.Limits.Services.Alerts;

public sealed class LimitsAlertsOptionsValidator : IValidateOptions<LimitsAlertsOptions>
{
	public ValidateOptionsResult Validate(string? name, LimitsAlertsOptions options) => options.WarningThresholdPercent > 0m && options.WarningThresholdPercent < 100m ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("LimitsAlerts:WarningThresholdPercent must be greater than 0 and less than 100.");
}
