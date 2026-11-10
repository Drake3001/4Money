namespace ZPI4M.User.Services.GoogleAuthentication;

public enum GoogleAuthenticationResult
{
	LoggedIn,
	Registered,
	InvalidCredentials,
	RegistrationRequired,
	AccountInactive,
	EmailConfirmationRequired,
	AccountAlreadyExists,
	LoginOrEmailAlreadyInUse,
	EmailDeliveryFailed
}
