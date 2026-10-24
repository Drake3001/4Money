namespace ZPI4M.User.DTOs.Registration;

public sealed record RegisterUserResponse(
	Guid Id,
	string Login,
	string Name,
	string Surname,
	string Email,
	DateOnly BirthDate,
	bool IsEmailConfirmed,
	DateTime CreatedAt);
