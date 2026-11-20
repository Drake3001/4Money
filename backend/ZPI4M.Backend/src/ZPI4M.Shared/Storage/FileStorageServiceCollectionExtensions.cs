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
			options.Endpoint = configuration["Storage:Endpoint"] ?? string.Empty;
			options.PublicEndpoint = configuration["Storage:PublicEndpoint"];
			options.Bucket = configuration["Storage:Bucket"] ?? string.Empty;
			options.Region = configuration["Storage:Region"] ?? string.Empty;
			options.AccessKey = configuration["Storage:AccessKey"] ?? string.Empty;
			options.SecretKey = configuration["Storage:SecretKey"] ?? string.Empty;
			var expiration = configuration["Storage:UrlExpirationMinutes"];
			options.UrlExpirationMinutes = expiration is null ? 15
				: int.TryParse(expiration, out var minutes) ? minutes : 0;
		}).ValidateOnStart();
		services.TryAddSingleton<TimeProvider>(TimeProvider.System);
		services.AddSingleton<IAmazonS3>(provider => provider.GetRequiredService<IOptions<S3StorageOptions>>().Value.CreateClient());
		services.AddScoped<IObjectStorage, S3ObjectStorage>();
		return services;
	}
}
