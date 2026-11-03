using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;

namespace ZPI4M.Shared.Storage;

public sealed class S3StorageOptions
{
	public string Endpoint { get; set; } = string.Empty;
	public string? PublicEndpoint { get; set; }
	public string Bucket { get; set; } = string.Empty;
	public string Region { get; set; } = string.Empty;
	public string AccessKey { get; set; } = string.Empty;
	public string SecretKey { get; set; } = string.Empty;
	public int UrlExpirationMinutes { get; set; } = 15;

	public IAmazonS3 CreateClient(bool usePublicEndpoint = false)
	{
		var validation = new S3StorageOptionsValidator().Validate(null, this);
		if (validation.Failed)
			throw new OptionsValidationException(Options.DefaultName, typeof(S3StorageOptions), validation.Failures);

		return new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey), new AmazonS3Config
		{
			ServiceURL = usePublicEndpoint ? PublicEndpoint ?? Endpoint : Endpoint,
			AuthenticationRegion = Region,
			ForcePathStyle = true
		});
	}
}

public sealed class S3StorageOptionsValidator : IValidateOptions<S3StorageOptions>
{
	public ValidateOptionsResult Validate(string? name, S3StorageOptions options)
	{
		var errors = new List<string>();
		if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
			|| (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
			|| string.IsNullOrEmpty(endpoint.Host) || !string.IsNullOrEmpty(endpoint.UserInfo)
			|| endpoint.AbsolutePath != "/" || !string.IsNullOrEmpty(endpoint.Query)
			|| !string.IsNullOrEmpty(endpoint.Fragment))
			errors.Add("STORAGE_ENDPOINT must be an absolute HTTP(S) origin without credentials, path, query or fragment.");
		if (options.PublicEndpoint is not null
			&& (!Uri.TryCreate(options.PublicEndpoint, UriKind.Absolute, out var publicEndpoint)
				|| (publicEndpoint.Scheme != Uri.UriSchemeHttp && publicEndpoint.Scheme != Uri.UriSchemeHttps)
				|| string.IsNullOrEmpty(publicEndpoint.Host) || !string.IsNullOrEmpty(publicEndpoint.UserInfo)
				|| publicEndpoint.AbsolutePath != "/" || !string.IsNullOrEmpty(publicEndpoint.Query)
				|| !string.IsNullOrEmpty(publicEndpoint.Fragment)))
			errors.Add("STORAGE_PUBLIC_ENDPOINT must be an absolute HTTP(S) origin without credentials, path, query or fragment.");
		if (string.IsNullOrWhiteSpace(options.Bucket))
			errors.Add("STORAGE_BUCKET is required.");
		if (string.IsNullOrWhiteSpace(options.Region))
			errors.Add("STORAGE_REGION is required.");
		if (string.IsNullOrWhiteSpace(options.AccessKey))
			errors.Add("STORAGE_ACCESS_KEY is required.");
		if (string.IsNullOrWhiteSpace(options.SecretKey))
			errors.Add("STORAGE_SECRET_KEY is required.");
		if (options.UrlExpirationMinutes is < 1 or > 10080)
			errors.Add("STORAGE_URL_EXPIRATION_MINUTES must be an integer between 1 and 10080.");
		return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
	}
}
