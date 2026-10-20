namespace ZPI4M.User.Integrations.Smtp;

public sealed class EmailDeliveryException : Exception
{
	public EmailDeliveryException(string message, Exception innerException) : base(message, innerException)
	{
	}
}
