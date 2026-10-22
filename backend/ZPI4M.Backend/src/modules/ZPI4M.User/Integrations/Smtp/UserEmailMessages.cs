namespace ZPI4M.User.Integrations.Smtp;

public sealed record EmailMessage(string Subject, string Body);

public static class UserEmailMessages
{
	public static EmailMessage EmailConfirmation(string token, DateTime expiresAt)
	{
		return new EmailMessage("4Money - potwierdź adres e-mail",
			$"Witaj w 4Money!\n\nAby potwierdzić adres e-mail, użyj poniższego tokenu:\n\n{token}\n\n" +
			$"Token jest ważny do {expiresAt:yyyy-MM-dd HH:mm:ss} UTC.\n\n" +
			"Jeśli nie rejestrowano konta w 4Money, możesz zignorować tę wiadomość.");
	}

	public static EmailMessage EmailChange(string token, DateTime expiresAt)
	{
		return new EmailMessage("4Money - potwierdź zmianę adresu e-mail",
			"Otrzymaliśmy prośbę o zmianę adresu e-mail konta w 4Money.\n\n" +
			$"Aby potwierdzić nowy adres e-mail, użyj poniższego tokenu:\n\n{token}\n\n" +
			$"Token jest ważny do {expiresAt:yyyy-MM-dd HH:mm:ss} UTC.\n\n" +
			"Do czasu potwierdzenia dotychczasowy adres pozostaje bez zmian.\n\n" +
			"Jeśli nie proszono o zmianę adresu, możesz zignorować tę wiadomość.");
	}

	public static EmailMessage PasswordReset(string token, DateTime expiresAt)
	{
		return new EmailMessage("4Money - reset hasła",
			"Otrzymaliśmy prośbę o reset hasła konta w 4Money.\n\n" +
			$"Aby ustawić nowe hasło, użyj poniższego tokenu:\n\n{token}\n\n" +
			$"Token jest ważny do {expiresAt:yyyy-MM-dd HH:mm:ss} UTC i można go użyć tylko raz.\n\n" +
			"Jeśli nie proszono o reset hasła, możesz zignorować tę wiadomość. Dotychczasowe hasło pozostaje bez zmian.");
	}
}
