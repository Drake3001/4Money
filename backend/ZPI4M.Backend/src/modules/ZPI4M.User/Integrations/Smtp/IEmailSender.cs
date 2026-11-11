namespace ZPI4M.User.Integrations.Smtp;

public interface IEmailSender
{
	/// <summary>Sends trusted HTML with a plain-text alternative. Success means SMTP acceptance, not inbox delivery.</summary>
	Task SendAsync(string recipientEmail, string subject, string body, CancellationToken cancellationToken = default);
}
