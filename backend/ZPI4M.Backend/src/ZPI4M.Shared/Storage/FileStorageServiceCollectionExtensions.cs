using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ZPI4M.Shared.Storage;

public static class FileStorageServiceCollectionExtensions
{
	public static IServiceCollection AddObjectStorage(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<IValidateOptions<S3StorageOptions>, S3StorageOptionsValidator>();
		services.AddOptions<S3StorageOptions>().Configure(options =>
		{
			options.Endpoint = configuration["STORAGE_ENDPOINT"] ?? string.Empty;
			options.PublicEndpoint = configuration["STORAGE_PUBLIC_ENDPOINT"];
			options.Bucket = configuration["STORAGE_BUCKET"] ?? string.Empty;
			options.Region = configuration["STORAGE_REGION"] ?? string.Empty;
			options.AccessKey = configuration["STORAGE_ACCESS_KEY"] ?? string.Empty;
			options.SecretKey = configuration["STORAGE_SECRET_KEY"] ?? string.Empty;
			var expiration = configuration["STORAGE_URL_EXPIRATION_MINUTES"];
			options.UrlExpirationMinutes = expiration is null ? 15
				: int.TryParse(expiration, out var minutes) ? minutes : 0;
		}).ValidateOnStart();
		services.TryAddSingleton<TimeProvider>(TimeProvider.System);
		services.AddSingleton<IAmazonS3>(provider => provider.GetRequiredService<IOptions<S3StorageOptions>>().Value.CreateClient());
		services.AddScoped<IObjectStorage, S3ObjectStorage>();
		return services;
	}
}
