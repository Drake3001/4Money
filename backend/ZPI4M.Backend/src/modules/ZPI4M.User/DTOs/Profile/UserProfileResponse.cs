namespace ZPI4M.User.DTOs.Profile;

public sealed record UserProfileResponse(
	Guid Id,
	string Login,
	string Name,
	string Surname,
	string Email,
	string? Phone,
	DateOnly BirthDate,
	string? Icon,
	bool IsEmailConfirmed,
	DateTime CreatedAt,
	DateTime? LastLoginTime);
