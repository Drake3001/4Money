namespace ZPI4M.User.DTOs.Authentication;

public sealed record AuthenticationResponse(
	string AccessToken,
	DateTime AccessTokenExpiresAt,
	string RefreshToken,
	DateTime RefreshTokenExpiresAt,
	string TokenType = "Bearer");
