using System.Net;
using Microsoft.Extensions.Options;

namespace ZPI4M.User.Integrations.Smtp;

public sealed record EmailMessage(string Subject, string Body);

public sealed class UserEmailMessages(IOptions<UserEmailLinkOptions> options)
{
	internal const string LogoContentId = "4money-logo";
	private static readonly TimeZoneInfo PolishTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

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
		"Przycisk otworzy formularz pierwszego hasła. Twoje prywatne hasło do konta Google pozostanie bez zmian. Po zapisaniu hasła obecne sesje 4Money zostaną unieważnione. Możesz wtedy logować się hasłem lub przez Google. Jeśli nie proszono o ustawienie hasła, zignoruj tę wiadomość.");

	private EmailMessage Create(string title, string action, string token, DateTime expiresAt, string intro, string details)
	{
		var url = $"{options.Value.FrontendBaseUrl.TrimEnd('/')}/?action={action}#token={Uri.EscapeDataString(token)}";
		var safeUrl = WebUtility.HtmlEncode(url);
		var utcExpiry = expiresAt.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)
			: expiresAt.ToUniversalTime();
		var polishExpiry = TimeZoneInfo.ConvertTimeFromUtc(utcExpiry, PolishTimeZone);
		var html = $"""
			<!doctype html>
			<html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
			<body style="margin:0;background:#f5f8fa;font-family:Arial,sans-serif;color:#07183a;padding:32px 16px">
			<table role="presentation" style="width:100%;max-width:520px;margin:0 auto;background:#ffffff;border-radius:16px"><tr><td style="padding:32px">
			<table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 0 24px"><tr>
			<td style="vertical-align:middle;padding-right:12px"><img src="cid:{LogoContentId}" alt="" width="48" height="48" style="display:block;border:0;width:48px;height:48px"></td>
			<td style="vertical-align:middle;font-size:26px;font-weight:bold;color:#07183a">4Money</td>
			</tr></table>
			<h1 style="font-size:24px;line-height:1.3">{WebUtility.HtmlEncode(title)}</h1>
			<p style="line-height:1.6">{WebUtility.HtmlEncode(intro)}</p>
			<p style="line-height:1.6">{WebUtility.HtmlEncode(details)}</p>
			<p style="margin:28px 0"><a href="{safeUrl}" style="display:inline-block;padding:14px 22px;border-radius:10px;background:#07183a;color:#22d9a2;text-decoration:none;font-weight:bold">{WebUtility.HtmlEncode(title)}</a></p>
			<p style="margin:0;font-size:13px;line-height:1.6;color:#52607d">Link jest jednorazowy i ważny do {polishExpiry:yyyy-MM-dd HH:mm:ss}.</p>
			</td></tr></table></body></html>
			""";
		return new EmailMessage($"4Money - {title}", html);
	}
}
