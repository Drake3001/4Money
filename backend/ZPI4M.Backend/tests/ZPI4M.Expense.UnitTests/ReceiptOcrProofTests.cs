using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Expense.Services.Expenses;

namespace ZPI4M.Expense.UnitTests;

public sealed class ReceiptOcrProofTests
{
	[Fact]
	public void ProofExpiresAfterTwentyFourHours()
	{
		var options = Options.Create(new ReceiptOcrProofOptions
		{
			SigningKey = Convert.ToBase64String(new byte[32])
		});
		var clock = new TestClock();
		var service = new ReceiptOcrProof(options, clock);
		var actor = Guid.NewGuid();
		using var result = JsonDocument.Parse("""{"receipt":{"store":"Shop"}}""");
		var proof = service.Create(actor, "file-hash", result.RootElement)!;
		Assert.Equal(result.RootElement.GetRawText(), service.Verify(proof, actor, "file-hash"));
		clock.Now = clock.Now.AddHours(24);
		Assert.Null(service.Verify(proof, actor, "file-hash"));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(31)]
	[InlineData(32)]
	[InlineData(64)]
	public void ConfigurationRequiresAtLeastThirtyTwoDecodedBytes(int length)
	{
		var options = new ReceiptOcrProofOptions { SigningKey = Convert.ToBase64String(new byte[length]) };
		var result = new ReceiptOcrProofOptionsValidator().Validate(null, options);
		Assert.Equal(length >= 32, result.Succeeded);
	}

	[Fact]
	public void ConfigurationRejectsMalformedBase64WithoutExposingTheKey()
	{
		const string invalid = "invalid-secret-key";
		var result = new ReceiptOcrProofOptionsValidator().Validate(null, new ReceiptOcrProofOptions { SigningKey = invalid });
		Assert.True(result.Failed);
		Assert.Contains("RECEIPTOCRPROOF__SIGNINGKEY", result.FailureMessage);
		Assert.DoesNotContain(invalid, result.FailureMessage);
	}

	[Fact]
	public void RegistrationValidatesTheSigningKeyAtStartup()
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
		var services = new ServiceCollection();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddExpenseServices();
		using var provider = services.BuildServiceProvider();
		var startup = provider.GetRequiredService<IStartupValidator>();
		Assert.Throws<OptionsValidationException>(() => startup.Validate());
	}

	private sealed class TestClock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
		public override DateTimeOffset GetUtcNow() => Now;
	}
}
