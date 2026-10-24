namespace ZPI4M.User.Services.RateLimiting;

public static class UserRateLimitingPolicies
{
	public const string Login = "user-login";
	public const string Refresh = "user-refresh";
	public const string PasswordReset = "user-password-reset";
	public const string Email = "user-email";
}
