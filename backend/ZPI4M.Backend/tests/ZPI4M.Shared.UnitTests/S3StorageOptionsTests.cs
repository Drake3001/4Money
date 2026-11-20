using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Shared.Storage;


namespace ZPI4M.Shared.UnitTests;

public sealed class S3StorageOptionsTests
{
	[Theory]
	[InlineData("")]
	[InlineData("ftp://storage.example.test")]
	[InlineData("http://user:secret@storage.example.test")]
	[InlineData("http://storage.example.test/bucket")]
	[InlineData("http://storage.example.test?secret=value")]
	[InlineData("http://storage.example.test#fragment")]
	public void Invalid_public_endpoint_is_rejected_without_echoing_value(string endpoint)
	{
		var options = S3ObjectStorageTests.ValidOptions();
		options.PublicEndpoint = endpoint;
		var error = Assert.Throws<OptionsValidationException>(() => options.CreateClient());
		Assert.Contains("STORAGE_PUBLIC_ENDPOINT", error.Message);
		Assert.DoesNotContain("secret", error.Message);
	}

	[Theory]
	[InlineData("http://127.0.0.1:8333")]
	[InlineData("https://storage.example.test")]
	public void Valid_configuration_creates_path_style_client(string endpoint)
	{
		var options = S3ObjectStorageTests.ValidOptions();
		options.Endpoint = endpoint;
		using var client = options.CreateClient();
		Assert.True(((AmazonS3Config)client.Config).ForcePathStyle);
		Assert.Equal("us-east-1", client.Config.AuthenticationRegion);
		Assert.Equal(endpoint.TrimEnd('/'), client.Config.ServiceURL.TrimEnd('/'));
	}

	[Theory]
	[InlineData("")]
	[InlineData("ftp://storage.example.test")]
	[InlineData("http://user:secret@storage.example.test")]
	[InlineData("http://storage.example.test/bucket")]
	[InlineData("http://storage.example.test?secret=value")]
	[InlineData("http://storage.example.test#fragment")]
	public void Invalid_endpoint_is_rejected_without_echoing_value(string endpoint)
	{
		var options = S3ObjectStorageTests.ValidOptions();
		options.Endpoint = endpoint;
		var error = Assert.Throws<OptionsValidationException>(() => options.CreateClient());
		Assert.Contains("STORAGE_ENDPOINT", error.Message);
		Assert.DoesNotContain("secret", error.Message);
	}

	[Theory]
	[InlineData("Storage:Bucket", "STORAGE_BUCKET")]
	[InlineData("Storage:Region", "STORAGE_REGION")]
	[InlineData("Storage:AccessKey", "STORAGE_ACCESS_KEY")]
	[InlineData("Storage:SecretKey", "STORAGE_SECRET_KEY")]
	[InlineData("Storage:Endpoint", "STORAGE_ENDPOINT")]
	public void Missing_configuration_fails_startup_without_exposing_credentials(string missing, string diagnostic)
	{
		var values = Values();
		values.Remove(missing);
		using var provider = Provider(values);
		var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
		Assert.Contains(diagnostic, error.Message);
		Assert.DoesNotContain("synthetic-access", error.Message);
		Assert.DoesNotContain("synthetic-secret", error.Message);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("10081")]
	[InlineData("invalid-sensitive-value")]
	[InlineData("")]
	public void Invalid_expiration_fails_startup(string expiration)
	{
		var values = Values();
		values["Storage:UrlExpirationMinutes"] = expiration;
		using var provider = Provider(values);
		var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
		Assert.Contains("STORAGE_URL_EXPIRATION_MINUTES", error.Message);
		Assert.DoesNotContain("invalid-sensitive-value", error.Message);
	}

	[Theory]
	[InlineData(null, 15)]
	[InlineData("1", 1)]
	[InlineData("10080", 10080)]
	public void Registration_validates_defaults_and_lifetimes(string? expiration, int expected)
	{
		var values = Values();
		if (expiration is not null)
			values["Storage:UrlExpirationMinutes"] = expiration;
		using var provider = Provider(values);
		provider.GetRequiredService<IStartupValidator>().Validate();
		Assert.Equal(expected, provider.GetRequiredService<IOptions<S3StorageOptions>>().Value.UrlExpirationMinutes);
		using var first = provider.CreateScope();
		using var second = provider.CreateScope();
		Assert.IsType<S3ObjectStorage>(first.ServiceProvider.GetRequiredService<IObjectStorage>());
		Assert.Same(first.ServiceProvider.GetRequiredService<IAmazonS3>(), second.ServiceProvider.GetRequiredService<IAmazonS3>());
		Assert.NotSame(first.ServiceProvider.GetRequiredService<IObjectStorage>(), second.ServiceProvider.GetRequiredService<IObjectStorage>());
	}

	private static Dictionary<string, string?> Values() => new()
	{
		["Storage:Endpoint"] = "http://127.0.0.1:8333",
		["Storage:Bucket"] = "receipts",
		["Storage:Region"] = "us-east-1",
		["Storage:AccessKey"] = "synthetic-access",
		["Storage:SecretKey"] = "synthetic-secret"
	};

	private static ServiceProvider Provider(Dictionary<string, string?> values)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
		return new ServiceCollection().AddObjectStorage(configuration).BuildServiceProvider();
	}
}
