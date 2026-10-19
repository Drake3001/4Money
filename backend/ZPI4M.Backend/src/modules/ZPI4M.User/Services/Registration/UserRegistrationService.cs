using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.Registration;
using ZPI4M.User.Repositories;

namespace ZPI4M.User.Services.Registration;

public sealed class UserRegistrationService : IUserRegistrationService
{
	private readonly IUserRepository _users;
	private readonly UserDbContext _dbContext;
	private readonly IPasswordHasher<Models.User> _passwordHasher;

	public UserRegistrationService(
		IUserRepository users,
		UserDbContext dbContext,
		IPasswordHasher<Models.User> passwordHasher)
	{
		_users = users;
		_dbContext = dbContext;
		_passwordHasher = passwordHasher;
	}

	public async Task<UserRegistrationResult> RegisterAsync(
		RegisterUserRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (await _users.ExistsByLoginAsync(request.Login, cancellationToken) ||
			await _users.ExistsByEmailAsync(request.Email, cancellationToken))
		{
			return UserRegistrationResult.LoginOrEmailAlreadyInUse;
		}

		var user = new Models.User
		{
			Id = Guid.NewGuid(),
			Login = request.Login,
			Name = request.Name,
			Surname = request.Surname,
			Email = request.Email,
			BirthDate = request.BirthDate,
			IsEmailConfirmed = false,
			IsActive = true
		};
		user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

		_users.Add(user);
		try
		{
			await _dbContext.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException exception) when (exception.InnerException is PostgresException
		{
			SqlState: PostgresErrorCodes.UniqueViolation,
			ConstraintName: "c_user_unique_login" or "c_user_unique_email"
		})
		{
			_dbContext.Entry(user).State = EntityState.Detached;
			return UserRegistrationResult.LoginOrEmailAlreadyInUse;
		}

		return UserRegistrationResult.Registered;
	}
}
