using Microsoft.Extensions.Logging;
using ZPI4M.User.Integrations.Smtp;

namespace ZPI4M.User.Services.PasswordReset;

public sealed class PasswordResetMessageSender(
	IEmailSender emailSender,
	ILogger<PasswordResetMessageSender> logger) : IPasswordResetMessageSender
{
	public async Task SendAsync(string recipientEmail, string token, DateTime expiresAt, CancellationToken cancellationToken = default)
	{
		var body = "Otrzymaliśmy prośbę o reset hasła konta w 4Money.\n\n" +
			$"Aby ustawić nowe hasło, użyj poniższego tokenu:\n\n{token}\n\n" +
			$"Token jest ważny do {expiresAt:yyyy-MM-dd HH:mm:ss} UTC i można go użyć tylko raz.\n\n" +
			"Jeśli nie proszono o reset hasła, możesz zignorować tę wiadomość. Dotychczasowe hasło pozostaje bez zmian.";
		try
		{
			await emailSender.SendAsync(recipientEmail, "4Money - reset hasła", body, cancellationToken);
		}
		catch (EmailDeliveryException)
		{
			logger.LogWarning("Password reset email delivery failed.");
		}
	}
}
