using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ZPI4M.User.Integrations.Smtp;

public sealed class SmtpEmailSender : IEmailSender
{
	private readonly IOptions<SmtpOptions> _options;

	public SmtpEmailSender(IOptions<SmtpOptions> options)
	{
		_options = options;
	}

	public async Task SendAsync(string recipientEmail, string subject, string body, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(recipientEmail);
		ArgumentException.ThrowIfNullOrWhiteSpace(subject);
		ArgumentNullException.ThrowIfNull(body);

		try
		{
			var options = _options.Value;
			using var message = new MimeMessage();
			message.From.Add(new MailboxAddress(options.SenderName, options.SenderEmail));
			message.To.Add(MailboxAddress.Parse(recipientEmail));
			message.Subject = subject;
			var textWithLinks = System.Text.RegularExpressions.Regex.Replace(body,
				"<a\\b[^>]*href=\"([^\"]*)\"[^>]*>(.*?)</a>", "$2: $1",
				System.Text.RegularExpressions.RegexOptions.Singleline);
			var builder = new BodyBuilder
			{
				HtmlBody = body,
				TextBody = System.Net.WebUtility.HtmlDecode(
					System.Text.RegularExpressions.Regex.Replace(textWithLinks, "<[^>]+>", "\n"))
			};
			using var logoStream = typeof(UserEmailMessages).Assembly.GetManifestResourceStream("ZPI4M.User.EmailLogo.png")
				?? throw new InvalidOperationException("Email logo resource is missing.");
			if (body.Contains($"cid:{UserEmailMessages.LogoContentId}", StringComparison.Ordinal))
			{
				var logo = builder.LinkedResources.Add("4money-logo.png", logoStream, new ContentType("image", "png"));
				logo.ContentId = UserEmailMessages.LogoContentId;
			}
			message.Body = builder.ToMessageBody();

			var security = options.SecurityMode switch
			{
				SmtpSecurityMode.None => SecureSocketOptions.None,
				SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
				SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
				_ => throw new InvalidOperationException("Unsupported SMTP security mode.")
			};

			using var client = new SmtpClient
			{
				Timeout = options.TimeoutSeconds * 1000,
				CheckCertificateRevocation = options.CheckCertificateRevocation
			};
			await client.ConnectAsync(options.Host, options.Port, security, cancellationToken);
			if (!string.IsNullOrWhiteSpace(options.Username))
			{
				await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
			}

			await client.SendAsync(message, cancellationToken);
			// The server has accepted DATA. Close locally without a second network operation
			// that could report a false delivery failure after successful acceptance.
			await client.DisconnectAsync(false, CancellationToken.None);
		}
		catch (SslHandshakeException exception)
		{
			throw new EmailDeliveryException("SMTP TLS handshake failed.", exception);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			throw new EmailDeliveryException("SMTP message sending failed.", exception);
		}
	}
}
