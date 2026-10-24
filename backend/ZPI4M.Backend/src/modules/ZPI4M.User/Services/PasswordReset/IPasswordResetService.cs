using ZPI4M.User.DTOs.PasswordReset;

namespace ZPI4M.User.Services.PasswordReset;

public interface IPasswordResetService
{
	Task RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
	Task<PasswordResetResult> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

public enum PasswordResetResult
{
	Reset,
	InvalidOrExpiredToken
}
