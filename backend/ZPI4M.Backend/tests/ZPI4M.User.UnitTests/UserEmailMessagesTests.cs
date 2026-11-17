using System.Net;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.User.Integrations.Smtp;

namespace ZPI4M.User.UnitTests;

public sealed class UserEmailMessagesTests
{
	[Theory]
	[InlineData("confirm-email", false)]
	[InlineData("confirm-email-change", false)]
	[InlineData("confirm-deactivation", false)]
	[InlineData("reset", false)]
	[InlineData("reset", true)]
	public void HtmlEmailsLinkToConfiguredFrontendWithAnEncodedToken(string action, bool firstPassword)
	{
		var messages = new UserEmailMessages(Options.Create(new UserEmailLinkOptions
		{
			FrontendBaseUrl = "https://server.tail000000.ts.net/"
		}));
		const string token = "synthetic+token/&\"<>";
		var expires = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
		var message = action switch
		{
			"confirm-email" => messages.EmailConfirmation(token, expires),
			"confirm-email-change" => messages.EmailChange(token, expires),
			"confirm-deactivation" => messages.AccountDeactivation(token, expires),
			_ => firstPassword ? messages.PasswordSetup(token, expires) : messages.PasswordReset(token, expires)
		};
		var url = $"https://server.tail000000.ts.net/?action={action}#token={Uri.EscapeDataString(token)}";
		Assert.Contains($"href=\"{WebUtility.HtmlEncode(url)}\"", message.Body);
		Assert.DoesNotContain(token, message.Body);
		Assert.Contains("<html lang=\"pl\">", message.Body);
		Assert.Contains("2026-11-05 13:00:00.", message.Body);
		Assert.StartsWith("4Money - ", message.Subject);
	}

	[Theory]
	[InlineData("http://localhost:5173", true)]
	[InlineData("https://server.tail000000.ts.net", true)]
	[InlineData("/relative", false)]
	[InlineData("javascript:alert(1)", false)]
	[InlineData("https://example.test/?token=secret", false)]
	[InlineData("https://example.test/#fragment", false)]
	[InlineData("https://user:password@example.test", false)]
	public void FrontendUrlMustBeAbsoluteHttpWithoutQueryFragmentOrCredentials(string value, bool expected)
	{
		Assert.Equal(expected, UserEmailLinkOptions.IsValid(new UserEmailLinkOptions { FrontendBaseUrl = value }));
	}
}
