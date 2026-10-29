using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ZPI4M.Receipt.Services;

public static class ReceiptAnalysisServiceCollectionExtensions
{
	public static IServiceCollection AddReceiptAnalysis(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<IValidateOptions<ReceiptAnalysisOptions>, ReceiptAnalysisOptionsValidator>();
		services.AddOptions<ReceiptAnalysisOptions>().Configure(options =>
		{
			options.BaseUrl = configuration["OCR_BASE_URL"] ?? string.Empty;
			var timeout = configuration["OCR_TIMEOUT_SECONDS"];
			options.TimeoutSeconds = timeout is null ? 120
				: int.TryParse(timeout, out var seconds) ? seconds : 0;
		}).ValidateOnStart();
		services.AddHttpClient<IReceiptAnalysisService, HttpReceiptAnalysisService>((provider, client) =>
		{
			var options = provider.GetRequiredService<IOptions<ReceiptAnalysisOptions>>().Value;
			client.BaseAddress = new Uri(options.BaseUrl);
			client.Timeout = Timeout.InfiniteTimeSpan;
		}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
		{
			AllowAutoRedirect = false,
			UseCookies = false
		});
		return services;
	}
}
