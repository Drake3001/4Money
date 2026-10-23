using ZPI4M.User.DTOs.Authentication;

namespace ZPI4M.User.Services.Authentication;

public interface IUserAuthenticationService
{
	Task<(LoginResult Result, AuthenticationResponse? Response)> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
	Task<AuthenticationResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
	Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
	Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
}

public enum LoginResult
{
	LoggedIn,
	InvalidCredentials,
	AccountInactive
}
