using ZPI4M.User.DTOs.Registration;

namespace ZPI4M.User.Services.Registration;

public interface IUserRegistrationService
{
	Task<UserRegistrationResult> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken = default);
}

public enum UserRegistrationResult
{
	Registered,
	LoginOrEmailAlreadyInUse
}
