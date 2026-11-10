using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.Registration;
using ZPI4M.User.Integrations.Smtp;
using ZPI4M.User.Models;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Services.EmailConfirmation;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.User.Services.Registration;

public sealed class UserRegistrationService : IUserRegistrationService
{
	private readonly IUserRepository _users;
	private readonly UserDbContext _dbContext;
	private readonly IPasswordHasher<Models.User> _passwordHasher;
	private readonly IUserTokenGenerator _tokenGenerator;
	private readonly IEmailSender _emailSender;
	private readonly EmailConfirmationOptions _confirmationOptions;

	public UserRegistrationService(
		IUserRepository users,
		UserDbContext dbContext,
		IPasswordHasher<Models.User> passwordHasher,
		IUserTokenGenerator tokenGenerator,
		IEmailSender emailSender,
		IOptions<EmailConfirmationOptions> confirmationOptions)
	{
		_users = users;
		_dbContext = dbContext;
		_passwordHasher = passwordHasher;
		_tokenGenerator = tokenGenerator;
		_emailSender = emailSender;
		_confirmationOptions = confirmationOptions.Value;
	}

	public async Task<(UserRegistrationResult Result, RegisterUserResponse? User)> RegisterAsync(
		RegisterUserRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (await _users.ExistsByLoginAsync(request.Login, cancellationToken) ||
			await _users.ExistsActiveByEmailAsync(request.Email, cancellationToken))
		{
			return (UserRegistrationResult.LoginOrEmailAlreadyInUse, null);
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
		var rawToken = _tokenGenerator.GenerateToken();
		var now = DateTime.UtcNow;
		var confirmationToken = new UserToken
		{
			Id = Guid.NewGuid(),
			FkUserId = user.Id,
			TokenHash = _tokenGenerator.HashToken(rawToken),
			Purpose = UserTokenPurpose.EmailConfirmation,
			TargetEmail = user.Email,
			CreatedAt = now,
			ExpiresAt = now.AddHours(_confirmationOptions.TokenLifetimeHours)
		};
		user.UserTokens.Add(confirmationToken);

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
			_dbContext.Entry(confirmationToken).State = EntityState.Detached;
			_dbContext.Entry(user).State = EntityState.Detached;
			return (UserRegistrationResult.LoginOrEmailAlreadyInUse, null);
		}

		var message = UserEmailMessages.EmailConfirmation(rawToken, confirmationToken.ExpiresAt);
		try
		{
			await _emailSender.SendAsync(user.Email, message.Subject, message.Body, cancellationToken);
		}
		catch (EmailDeliveryException)
		{
			return (UserRegistrationResult.EmailDeliveryFailed, null);
		}

		var response = new RegisterUserResponse(
			user.Id, user.Login, user.Name, user.Surname, user.Email,
			user.BirthDate, user.IsEmailConfirmed, user.CreatedAt);
		return (UserRegistrationResult.Registered, response);
	}
}
