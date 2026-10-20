using ZPI4M.User.DTOs.EmailConfirmation;

namespace ZPI4M.User.Services.EmailConfirmation;

public interface IEmailConfirmationService
{
	Task<EmailConfirmationResult> ConfirmAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default);
}

public enum EmailConfirmationResult
{
	Confirmed,
	InvalidOrExpiredToken
}
