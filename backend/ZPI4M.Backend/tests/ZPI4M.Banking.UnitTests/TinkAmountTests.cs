using Xunit;
using ZPI4M.Banking.Tink;
using ZPI4M.Banking.Tink.Http;

namespace ZPI4M.Banking.UnitTests;

public sealed class TinkAmountTests
{
	[Theory]
	[InlineData("-12345", "2", "-123.45")]
	[InlineData("77", "-3", "77000")]
	[InlineData("12505", "1", "1250.5")]
	[InlineData("12300", "4", "1.23")]
	[InlineData("999999999999999999", "2", "9999999999999999.99")]
	public void AmountsAreConvertedExactly(string value, string scale, string expected) => Assert.Equal(
		decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), TinkDataGateway.ParseAmount(value, scale));

	[Theory]
	[InlineData("12345", "3")]
	[InlineData("1000000000000000000", "2")]
	[InlineData("1", "-1000")]
	[InlineData("1.23", "2")]
	[InlineData(null, "2")]
	public void UnsupportedAmountsAreNotRoundedOrOverflowed(string? value, string scale) =>
		Assert.Throws<TinkApiException>(() => TinkDataGateway.ParseAmount(value, scale));
}
