using System.ComponentModel.DataAnnotations;

namespace ZPI4M.User.Integrations.Smtp;

public sealed class SmtpOptions
{
	public const string SectionName = "Smtp";

	[Required]
	public string Host { get; set; } = string.Empty;

	[Range(1, 65535)]
	public int Port { get; set; } = 587;

	[EnumDataType(typeof(SmtpSecurityMode))]
	public SmtpSecurityMode SecurityMode { get; set; } = SmtpSecurityMode.StartTls;

	public bool CheckCertificateRevocation { get; set; } = true;

	public string Username { get; set; } = string.Empty;

	public string Password { get; set; } = string.Empty;

	[Required, EmailAddress]
	public string SenderEmail { get; set; } = string.Empty;

	public string SenderName { get; set; } = "4Money";

	[Range(1, 300)]
	public int TimeoutSeconds { get; set; } = 30;
}

public enum SmtpSecurityMode
{
	None,
	StartTls,
	SslOnConnect
}
