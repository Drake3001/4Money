using ZPI4M.User.DTOs.Authentication;

namespace ZPI4M.User.DTOs.Registration;

public sealed record GoogleRegistrationResponse(
	RegisterUserResponse User,
	AuthenticationResponse? Authentication,
	bool RequiresEmailConfirmation);
