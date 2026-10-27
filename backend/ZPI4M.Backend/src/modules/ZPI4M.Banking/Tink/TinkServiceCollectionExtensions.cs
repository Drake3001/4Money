using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ZPI4M.Banking.Tink.Authentication;
using ZPI4M.Banking.Tink.Configuration;
using ZPI4M.Banking.Tink.Http;
using ZPI4M.Banking.Tink.Link;

namespace ZPI4M.Banking.Tink;

public static class TinkServiceCollectionExtensions
{
	public static IServiceCollection AddTink(this IServiceCollection services, IConfiguration configuration)
	{
		var section = configuration.GetSection(TinkOptions.SectionName);
		services.AddOptions<TinkOptions>().Bind(section)
			.PostConfigure(options =>
			{
				// Bind appends to initialized arrays; configured lists must replace defaults.
				options.ClientScopes = section.GetSection(nameof(TinkOptions.ClientScopes)).Get<string[]>() ?? options.ClientScopes;
				options.DelegatedScopes = section.GetSection(nameof(TinkOptions.DelegatedScopes)).Get<string[]>() ?? options.DelegatedScopes;
				options.RefreshableItems = section.GetSection(nameof(TinkOptions.RefreshableItems)).Get<string[]>() ?? options.RefreshableItems;
			})
			.Validate(options => options.IsValid(), "Tink configuration is missing or invalid.");
		services.AddMemoryCache();
		services.TryAddSingleton<TimeProvider>(TimeProvider.System);
		services.AddHttpClient(TinkHttp.ClientName, (provider, client) =>
		{
			var options = provider.GetRequiredService<IOptions<TinkOptions>>().Value;
			client.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
			client.Timeout = TimeSpan.FromSeconds(30);
		}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
			.RemoveAllLoggers();
		services.AddSingleton<TinkClientTokenProvider>();
		services.AddTransient<TinkAuthorizationService>();
		services.AddTransient<ITinkConnectionGateway, TinkConnectionGateway>();
		services.AddTransient<ITinkDataGateway, TinkDataGateway>();
		services.AddSingleton<TinkLinkUrlBuilder>();
		return services;
	}
}
