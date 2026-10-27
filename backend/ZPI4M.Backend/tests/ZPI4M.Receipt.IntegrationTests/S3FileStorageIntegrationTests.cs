using System.Net;
using DotNetEnv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZPI4M.Receipt.Services;

namespace ZPI4M.Receipt.IntegrationTests;

public sealed class S3FileStorageIntegrationTests
{
	[StorageIntegrationFact]
	public async Task Private_image_roundtrips_through_presigned_GET_and_can_be_deleted_twice()
	{
		var configurationBuilder = new ConfigurationBuilder();
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ZPI4M.Backend.slnx")))
			directory = directory.Parent;
		if (directory is not null)
		{
			var envPath = Path.Combine(directory.FullName, "src", "ZPI4M.Api", ".env");
			if (File.Exists(envPath))
				configurationBuilder.AddInMemoryCollection(Env.NoEnvVars().Load(envPath)
					.Where(pair => pair.Key.StartsWith("STORAGE_", StringComparison.Ordinal))
					.ToDictionary(pair => pair.Key, pair => (string?)pair.Value));
		}
		var configuration = configurationBuilder.AddEnvironmentVariables().Build();
		using var provider = new ServiceCollection().AddReceiptFileStorage(configuration).BuildServiceProvider();
		using var scope = provider.CreateScope();
		var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
		// Synthetic 1x1 PNG, no personal or financial data.
		var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
		using var input = new MemoryStream(image);
		using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
		string? key = null;
		await TestCleanup.RunAsync(async () =>
		{
			key = await storage.UploadAsync(input, "image/png", timeout.Token);
			var link = await storage.GetUrlAsync(key, timeout.Token);
			using var downloaded = await http.GetAsync(link.Url, timeout.Token);
			Assert.True(downloaded.IsSuccessStatusCode, $"Signed GET returned HTTP {(int)downloaded.StatusCode}.");
			Assert.Equal("image/png", downloaded.Content.Headers.ContentType?.MediaType);
			Assert.Equal(image, await downloaded.Content.ReadAsByteArrayAsync(timeout.Token));

			var anonymousUrl = new UriBuilder(link.Url) { Query = string.Empty }.Uri;
			using var anonymous = await http.GetAsync(anonymousUrl, timeout.Token);
			Assert.True(anonymous.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
				$"Anonymous GET returned HTTP {(int)anonymous.StatusCode} instead of denying access.");

			await storage.DeleteAsync(key, timeout.Token);
			using var deleted = await http.GetAsync(link.Url, timeout.Token);
			Assert.True(deleted.StatusCode == HttpStatusCode.NotFound, $"Deleted object returned HTTP {(int)deleted.StatusCode}.");
			await storage.DeleteAsync(key, timeout.Token);
		}, async () =>
		{
			if (key is not null)
			{
				using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
				await storage.DeleteAsync(key, cleanup.Token);
			}
		});
	}
}

public sealed class StorageIntegrationFactAttribute : FactAttribute
{
	public StorageIntegrationFactAttribute()
	{
		if (Environment.GetEnvironmentVariable("STORAGE_INTEGRATION_TESTS") != "1")
			Skip = "Manual test: start SeaweedFS and set STORAGE_INTEGRATION_TESTS=1; storage settings load from the API .env.";
	}
}
