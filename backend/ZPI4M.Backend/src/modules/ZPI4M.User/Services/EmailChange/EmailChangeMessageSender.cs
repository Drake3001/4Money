using ZPI4M.User.Integrations.Smtp;

namespace ZPI4M.User.Services.EmailChange;

public sealed class EmailChangeMessageSender(IEmailSender emailSender) : IEmailChangeMessageSender
{
	public async Task<bool> TrySendAsync(string recipientEmail, string token, DateTime expiresAt, CancellationToken cancellationToken = default)
	{
		var body = $"Otrzymaliśmy prośbę o zmianę adresu e-mail konta w 4Money.\n\n" +
			$"Aby potwierdzić nowy adres e-mail, użyj poniższego tokenu:\n\n{token}\n\n" +
			$"Token jest ważny do {expiresAt:yyyy-MM-dd HH:mm:ss} UTC.\n\n" +
			"Do czasu potwierdzenia dotychczasowy adres pozostaje bez zmian.\n\n" +
			"Jeśli nie proszono o zmianę adresu, możesz zignorować tę wiadomość.";
		try
		{
			await emailSender.SendAsync(recipientEmail, "4Money - potwierdź zmianę adresu e-mail", body, cancellationToken);
			return true;
		}
		catch (EmailDeliveryException)
		{
			return false;
		}
	}
}
