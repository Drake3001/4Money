namespace ZPI4M.User.Services.EmailChange;

public interface IEmailChangeMessageSender
{
	Task<bool> TrySendAsync(string recipientEmail, string token, DateTime expiresAt, CancellationToken cancellationToken = default);
}
