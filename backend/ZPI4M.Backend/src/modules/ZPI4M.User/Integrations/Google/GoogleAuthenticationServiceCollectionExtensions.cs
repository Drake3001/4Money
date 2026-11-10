using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ZPI4M.User.Services.GoogleAuthentication;

namespace ZPI4M.User.Integrations.Google;

public static class GoogleAuthenticationServiceCollectionExtensions
{
	public static IServiceCollection AddUserGoogleAuthentication(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddOptions<GoogleAuthenticationOptions>()
			.Bind(configuration.GetSection(GoogleAuthenticationOptions.SectionName))
			.ValidateDataAnnotations()
			.Validate(options => !options.Enabled || (!string.IsNullOrWhiteSpace(options.ClientId) &&
				options.ClientId == options.ClientId.Trim() && options.ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)),
				"GoogleAuthentication:ClientId must be a Google client ID when Google authentication is enabled.")
			.ValidateOnStart();
		services.TryAddSingleton<TimeProvider>(TimeProvider.System);
		services.AddSingleton<IGoogleAuthenticationChallengeStore, GoogleAuthenticationChallengeStore>();
		services.AddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
		services.AddScoped<IGoogleAuthenticationService, GoogleAuthenticationService>();
		services.AddHttpClient(GoogleIdTokenValidator.HttpClientName, (provider, client) =>
		{
			client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<GoogleAuthenticationOptions>>().Value.HttpTimeoutSeconds);
		});
		return services;
	}
}
