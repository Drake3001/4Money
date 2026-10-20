using ZPI4M.User.Integrations.Smtp;

namespace ZPI4M.User.Services.EmailConfirmation;

public sealed class EmailConfirmationMessageSender : IEmailConfirmationMessageSender
{
	private readonly IEmailSender _emailSender;

	public EmailConfirmationMessageSender(IEmailSender emailSender)
	{
		_emailSender = emailSender;
	}

	public async Task<bool> TrySendAsync(string recipientEmail, string token, DateTime expiresAt, CancellationToken cancellationToken = default)
	{
		var body = $"Witaj w 4Money!\n\nAby potwierdzić adres e-mail, użyj poniższego tokenu:\n\n{token}\n\n" +
			$"Token jest ważny do {expiresAt:yyyy-MM-dd HH:mm:ss} UTC.\n\n" +
			"Jeśli nie rejestrowano konta w 4Money, możesz zignorować tę wiadomość.";
		try
		{
			await _emailSender.SendAsync(recipientEmail, "4Money - potwierdź adres e-mail", body, cancellationToken);
			return true;
		}
		catch (EmailDeliveryException)
		{
			return false;
		}
	}
}
