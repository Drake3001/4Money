using System.Net;
using Microsoft.Extensions.Options;

namespace ZPI4M.User.Integrations.Smtp;

public sealed record EmailMessage(string Subject, string Body);

public sealed class UserEmailMessages(IOptions<UserEmailLinkOptions> options)
{
	public EmailMessage EmailConfirmation(string token, DateTime expiresAt) => Create(
		"Potwierdź adres e-mail", "confirm-email", token, expiresAt,
		"Witaj w 4Money! Potwierdź swój adres e-mail, aby móc się zalogować.",
		"Po potwierdzeniu przejdziesz do logowania. Jeśli nie zakładano konta w 4Money, zignoruj tę wiadomość.");

	public EmailMessage EmailChange(string token, DateTime expiresAt) => Create(
		"Potwierdź zmianę e-maila", "confirm-email-change", token, expiresAt,
		"Otrzymaliśmy prośbę o zmianę adresu e-mail Twojego konta w 4Money.",
		"Dotychczasowy adres pozostaje ważny do potwierdzenia. Po zmianie zaloguj się ponownie. Jeśli nie proszono o tę operację, zignoruj wiadomość.");

	public EmailMessage PasswordReset(string token, DateTime expiresAt) => Create(
		"Ustaw nowe hasło", "reset", token, expiresAt,
		"Otrzymaliśmy prośbę o reset hasła Twojego konta w 4Money.",
		"Przycisk otworzy formularz nowego hasła. Dotychczasowe hasło pozostaje ważne do zapisania nowego. Jeśli nie proszono o reset, zignoruj wiadomość.");

	public EmailMessage AccountDeactivation(string token, DateTime expiresAt) => Create(
		"Potwierdź dezaktywację konta", "confirm-deactivation", token, expiresAt,
		"Otrzymaliśmy prośbę o dezaktywację Twojego konta w 4Money.",
		"Kliknięcie przycisku i otwarcie aplikacji potwierdzi dezaktywację oraz zakończy sesje. Dane konta pozostaną w systemie. Jeśli nie proszono o dezaktywację, nie otwieraj linku.");

	public EmailMessage PasswordSetup(string token, DateTime expiresAt) => Create(
		"Ustaw pierwsze hasło", "reset", token, expiresAt,
		"Otrzymaliśmy prośbę o ustawienie pierwszego hasła dla Twojego konta Google w 4Money.",
		"Przycisk otworzy formularz pierwszego hasła do 4Money; hasło w Google pozostaje bez zmian. Po zapisaniu hasła obecne sesje zostaną unieważnione. Możesz wtedy logować się hasłem lub przez Google. Jeśli nie proszono o ustawienie hasła, zignoruj tę wiadomość.");

	private EmailMessage Create(string title, string action, string token, DateTime expiresAt, string intro, string details)
	{
		var url = $"{options.Value.FrontendBaseUrl.TrimEnd('/')}/?action={action}#token={Uri.EscapeDataString(token)}";
		var safeUrl = WebUtility.HtmlEncode(url);
		var html = $"""
			<!doctype html>
			<html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
			<body style="margin:0;background:#f3f6f5;font-family:Arial,sans-serif;color:#20372f;padding:32px 16px">
			<table role="presentation" style="width:100%;max-width:520px;margin:0 auto;background:#ffffff;border-radius:16px"><tr><td style="padding:32px">
			<p style="margin:0 0 24px;font-size:26px;font-weight:bold;color:#147d64">4Money</p>
			<h1 style="font-size:24px;line-height:1.3">{WebUtility.HtmlEncode(title)}</h1>
			<p style="line-height:1.6">{WebUtility.HtmlEncode(intro)}</p>
			<p style="line-height:1.6">{WebUtility.HtmlEncode(details)}</p>
			<p style="margin:28px 0"><a href="{safeUrl}" style="display:inline-block;padding:14px 22px;border-radius:10px;background:#147d64;color:#ffffff;text-decoration:none;font-weight:bold">{WebUtility.HtmlEncode(title)}</a></p>
			<p style="font-size:13px;color:#63756d">Link jest jednorazowy i ważny do {expiresAt:yyyy-MM-dd HH:mm:ss} UTC.</p>
			<p style="font-size:13px;color:#63756d">Jeśli przycisk nie działa, skopiuj cały adres do przeglądarki:</p>
			<p style="font-size:12px;word-break:break-all"><a href="{safeUrl}">{safeUrl}</a></p>
			</td></tr></table></body></html>
			""";
		return new EmailMessage($"4Money - {title}", html);
	}
}
