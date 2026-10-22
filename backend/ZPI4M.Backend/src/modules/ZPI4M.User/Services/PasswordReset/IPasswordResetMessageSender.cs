namespace ZPI4M.User.Services.PasswordReset;

public interface IPasswordResetMessageSender
{
	Task SendAsync(string recipientEmail, string token, DateTime expiresAt, CancellationToken cancellationToken = default);
}
