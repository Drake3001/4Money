using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.DTOs.Registration;

namespace ZPI4M.User.Services.GoogleAuthentication;

public interface IGoogleAuthenticationService
{
	Task<(GoogleAuthenticationResult Result, AuthenticationResponse? Response)> LoginAsync(
		GoogleLoginRequest request, string browserToken, CancellationToken cancellationToken = default);
	Task<(GoogleAuthenticationResult Result, GoogleRegistrationResponse? Response)> RegisterAsync(
		GoogleRegistrationRequest request, string browserToken, CancellationToken cancellationToken = default);
}
