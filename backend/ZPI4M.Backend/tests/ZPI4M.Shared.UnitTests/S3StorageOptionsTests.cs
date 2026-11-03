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
	[InlineData("STORAGE_BUCKET")]
	[InlineData("STORAGE_REGION")]
	[InlineData("STORAGE_ACCESS_KEY")]
	[InlineData("STORAGE_SECRET_KEY")]
	[InlineData("STORAGE_ENDPOINT")]
	public void Missing_configuration_fails_startup_without_exposing_credentials(string missing)
	{
		var values = Values();
		values.Remove(missing);
		using var provider = Provider(values);
		var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
		Assert.Contains(missing, error.Message);
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
		values["STORAGE_URL_EXPIRATION_MINUTES"] = expiration;
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
			values["STORAGE_URL_EXPIRATION_MINUTES"] = expiration;
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
		["STORAGE_ENDPOINT"] = "http://127.0.0.1:8333",
		["STORAGE_BUCKET"] = "receipts",
		["STORAGE_REGION"] = "us-east-1",
		["STORAGE_ACCESS_KEY"] = "synthetic-access",
		["STORAGE_SECRET_KEY"] = "synthetic-secret"
	};

	private static ServiceProvider Provider(Dictionary<string, string?> values)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
		return new ServiceCollection().AddObjectStorage(configuration).BuildServiceProvider();
	}
}
