using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Receipt.Services;

namespace ZPI4M.Receipt.UnitTests;

public sealed class ReceiptAnalysisOptionsTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("relative")]
	[InlineData("ftp://ocr.example.test")]
	[InlineData("https://user:secret@ocr.example.test")]
	[InlineData("https://ocr.example.test/api")]
	[InlineData("https://ocr.example.test?secret=value")]
	[InlineData("https://ocr.example.test#secret")]
	public void Invalid_origin_fails_startup_without_echoing_configuration(string? origin)
	{
		using var provider = Provider(origin);
		var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
		Assert.Contains("OCR_BASE_URL", error.Message);
		Assert.DoesNotContain("secret", error.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("1.5")]
	[InlineData("secret")]
	[InlineData("4294968")]
	[InlineData("2147483648")]
	public void Invalid_timeout_fails_startup(string timeout)
	{
		using var provider = Provider("http://ocr.example.test", timeout);
		var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
		Assert.Contains("OCR_TIMEOUT_SECONDS", error.Message);
		Assert.DoesNotContain("secret", error.Message);
	}

	[Theory]
	[InlineData("http://127.0.0.1:8000", null, 120)]
	[InlineData("https://ocr.example.test/", "1", 1)]
	[InlineData("https://ocr.example.test", "4294967", 4294967)]
	public void Registration_validates_and_resolves_real_client_without_network(string origin, string? timeout, int expected)
	{
		using var provider = Provider(origin, timeout);
		provider.GetRequiredService<IStartupValidator>().Validate();
		Assert.Equal(expected, provider.GetRequiredService<IOptions<ReceiptAnalysisOptions>>().Value.TimeoutSeconds);
		Assert.IsType<HttpReceiptAnalysisService>(provider.GetRequiredService<IReceiptAnalysisService>());
		var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IReceiptAnalysisService));
		using (client)
		{
			Assert.Equal(new Uri(origin), client.BaseAddress);
			Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
		}
	}

	private static ServiceProvider Provider(string? origin, string? timeout = null)
	{
		var values = new Dictionary<string, string?> { ["Ocr:BaseUrl"] = origin };
		if (timeout is not null)
			values["Ocr:TimeoutSeconds"] = timeout;
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
		return new ServiceCollection().AddReceiptAnalysis(configuration).BuildServiceProvider();
	}
}
