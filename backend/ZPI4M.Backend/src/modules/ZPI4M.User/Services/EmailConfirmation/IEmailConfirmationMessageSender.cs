namespace ZPI4M.User.Services.EmailConfirmation;

public interface IEmailConfirmationMessageSender
{
	Task<bool> TrySendAsync(string recipientEmail, string token, DateTime expiresAt, CancellationToken cancellationToken = default);
}
