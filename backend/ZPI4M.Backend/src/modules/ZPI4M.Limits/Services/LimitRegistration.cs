using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Grouping.Services.Access;
using ZPI4M.Limits.Repositories.GroupLimitRepository;
using ZPI4M.Limits.Repositories.LimitAlertRepository;
using ZPI4M.Limits.Repositories.LimitSweepRepository;
using ZPI4M.Limits.Repositories.UserLimitRepository;
using ZPI4M.Limits.Services.Alerts;
using ZPI4M.Limits.Services.Calendar;
using ZPI4M.Limits.Services.GroupLimits;
using ZPI4M.Limits.Services.Usage;
using ZPI4M.Limits.Services.UserLimits;
using ZPI4M.Limits.Services.Validation;

namespace ZPI4M.Limits.Services;

public static class LimitRegistration
{
	public static IServiceCollection AddLimits(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton<LimitCalendar>();
		services.AddSingleton<LimitRequestService>();
		services.AddScoped<LimitWriteExecutor>();
		services.AddScoped<IUserLimitRepository, UserLimitRepository>();
		services.AddScoped<IGroupLimitRepository, GroupLimitRepository>();
		services.AddScoped<IUserLimitService, UserLimitService>();
		services.AddScoped<IGroupLimitService, GroupLimitService>();
		services.AddScoped<LimitUsageService>();
		services.AddScoped<ILimitAlertRepository, LimitAlertRepository>();
		services.AddScoped<LimitAlertService>();
		services.AddScoped<IExpenseChangeHandler>(p => p.GetRequiredService<LimitAlertService>());
		services.AddScoped<IGroupBudgetContextService, GroupBudgetContextService>();
		services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<LimitsAlertsOptions>, LimitsAlertsOptionsValidator>();
		services.AddOptions<LimitsAlertsOptions>().BindConfiguration("LimitsAlerts").ValidateOnStart();
		services.AddOptions<LimitsAlertsSweepOptions>().BindConfiguration("LimitsAlertsSweep").Validate(o => o.IntervalSeconds is > 0 and <= 86400 && o.RunTimeoutSeconds is > 0 and <= 86400 && o.MaxDefinitionsPerRun is > 0 and <= 10000, "LimitsAlertsSweep requires times in 1..86400 seconds and MaxDefinitionsPerRun in 1..10000.").ValidateOnStart();
		services.AddScoped<ILimitSweepRepository, LimitSweepRepository>();
		services.AddScoped<LimitAlertSweepStep>();
		services.AddSingleton<ILimitAlertSweepService, LimitAlertSweepService>();
		services.AddHostedService<LimitAlertSweepWorker>();
		return services;
	}
}
