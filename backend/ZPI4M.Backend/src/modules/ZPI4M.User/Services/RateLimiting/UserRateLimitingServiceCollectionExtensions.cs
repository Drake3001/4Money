using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ZPI4M.User.Services.RateLimiting;

public static class UserRateLimitingServiceCollectionExtensions
{
	public static IServiceCollection AddUserRateLimiting(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddOptions<UserRateLimitingOptions>()
			.Bind(configuration.GetSection(UserRateLimitingOptions.SectionName))
			.ValidateDataAnnotations()
			.ValidateOnStart();
		services.AddRateLimiter();
		services.AddOptions<RateLimiterOptions>()
			.Configure<IOptions<UserRateLimitingOptions>>((limiter, configured) =>
			{
				var settings = configured.Value;
				limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
				AddPolicy(limiter, UserRateLimitingPolicies.Login, settings.LoginPermitLimit);
				AddPolicy(limiter, UserRateLimitingPolicies.Refresh, settings.RefreshPermitLimit);
				AddPolicy(limiter, UserRateLimitingPolicies.PasswordReset, settings.PasswordResetPermitLimit);
				AddPolicy(limiter, UserRateLimitingPolicies.Email, 5);
			});
		return services;
	}

	private static void AddPolicy(RateLimiterOptions options, string name, int permitLimit)
	{
		options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
			context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
			_ => new FixedWindowRateLimiterOptions
			{
				PermitLimit = permitLimit,
				Window = TimeSpan.FromMinutes(1),
				QueueLimit = 0
			}));
	}
}
