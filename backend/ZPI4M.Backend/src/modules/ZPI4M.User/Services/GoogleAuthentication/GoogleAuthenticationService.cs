using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.DTOs.Registration;
using ZPI4M.User.Integrations.Google;
using ZPI4M.User.Integrations.Smtp;
using ZPI4M.User.Models;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Services.Authentication;
using ZPI4M.User.Services.EmailConfirmation;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.User.Services.GoogleAuthentication;

public sealed class GoogleAuthenticationService(
	IGoogleIdTokenValidator validator,
	IGoogleAuthenticationChallengeStore challenges,
	IUserRepository users,
	UserDbContext dbContext,
	IUserSessionService sessions,
	IUserTokenGenerator tokenGenerator,
	IEmailSender emailSender,
	UserEmailMessages emailMessages,
	IOptions<EmailConfirmationOptions> confirmationOptions) : IGoogleAuthenticationService
{
	public async Task<(GoogleAuthenticationResult Result, AuthenticationResponse? Response)> LoginAsync(
		GoogleLoginRequest request, string browserToken, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var identity = await ValidateAsync(request.IdToken, browserToken, cancellationToken);
		if (identity is null)
			return (GoogleAuthenticationResult.InvalidCredentials, null);
		var candidate = await users.GetByGoogleSubjectAsync(identity.Subject, cancellationToken);
		if (candidate is null)
			return (GoogleAuthenticationResult.RegistrationRequired, null);

		await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
		var user = await users.GetByIdForUpdateAsync(candidate.Id, cancellationToken);
		if (user is null || user.GoogleSubject != identity.Subject)
			return (GoogleAuthenticationResult.InvalidCredentials, null);
		if (!user.IsActive)
			return (GoogleAuthenticationResult.AccountInactive, null);
		if (!user.IsEmailConfirmed)
			return (GoogleAuthenticationResult.EmailConfirmationRequired, null);
		if (!Consume(browserToken))
			return (GoogleAuthenticationResult.InvalidCredentials, null);

		var now = DateTime.UtcNow;
		var response = await sessions.CreateAsync(user.Id, now, cancellationToken);
		await users.RecordLoginAsync(user.Id, user.PasswordHash, now, cancellationToken);
		await dbContext.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return (GoogleAuthenticationResult.LoggedIn, response);
	}

	public async Task<(GoogleAuthenticationResult Result, GoogleRegistrationResponse? Response)> RegisterAsync(
		GoogleRegistrationRequest request, string browserToken, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var identity = await ValidateAsync(request.IdToken, browserToken, cancellationToken);
		if (identity is null)
			return (GoogleAuthenticationResult.InvalidCredentials, null);
		var existing = await users.GetByGoogleSubjectAsync(identity.Subject, cancellationToken);
		if (existing is not null)
			return (existing.IsActive ? GoogleAuthenticationResult.AccountAlreadyExists : GoogleAuthenticationResult.AccountInactive, null);
		if (await users.ExistsByLoginAsync(request.Login, cancellationToken) ||
			await users.ExistsActiveByEmailAsync(identity.Email, cancellationToken))
			return (GoogleAuthenticationResult.LoginOrEmailAlreadyInUse, null);

		var emailConfirmed = identity.EmailVerified &&
			(identity.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(identity.HostedDomain));
		var now = DateTime.UtcNow;
		var user = new Models.User
		{
			Id = Guid.NewGuid(),
			Login = request.Login,
			Name = request.Name,
			Surname = request.Surname,
			Email = identity.Email,
			GoogleSubject = identity.Subject,
			BirthDate = request.BirthDate,
			IsActive = true,
			IsEmailConfirmed = emailConfirmed,
			LastLoginTime = emailConfirmed ? now : null
		};
		string? rawToken = null;
		UserToken? confirmation = null;
		if (!emailConfirmed)
		{
			rawToken = tokenGenerator.GenerateToken();
			confirmation = new UserToken
			{
				Id = Guid.NewGuid(),
				FkUserId = user.Id,
				Purpose = UserTokenPurpose.EmailConfirmation,
				TargetEmail = user.Email,
				TokenHash = tokenGenerator.HashToken(rawToken),
				CreatedAt = now,
				ExpiresAt = now.AddHours(confirmationOptions.Value.TokenLifetimeHours)
			};
			user.UserTokens.Add(confirmation);
		}
		if (!Consume(browserToken))
			return (GoogleAuthenticationResult.InvalidCredentials, null);

		AuthenticationResponse? authentication = null;
		await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
		{
			users.Add(user);
			try
			{
				await dbContext.SaveChangesAsync(cancellationToken);
				if (emailConfirmed)
				{
					authentication = await sessions.CreateAsync(user.Id, now, cancellationToken);
					await dbContext.SaveChangesAsync(cancellationToken);
				}
				await transaction.CommitAsync(cancellationToken);
			}
			catch (DbUpdateException exception) when (exception.InnerException is PostgresException
			{
				SqlState: PostgresErrorCodes.UniqueViolation,
				ConstraintName: "c_user_unique_login" or "c_user_unique_email" or "c_user_unique_google_subject"
			})
			{
				if (confirmation is not null)
					dbContext.Entry(confirmation).State = EntityState.Detached;
				dbContext.Entry(user).State = EntityState.Detached;
				return (exception.InnerException is PostgresException { ConstraintName: "c_user_unique_google_subject" }
					? GoogleAuthenticationResult.AccountAlreadyExists : GoogleAuthenticationResult.LoginOrEmailAlreadyInUse, null);
			}
		}

		if (confirmation is not null)
		{
			var message = emailMessages.EmailConfirmation(rawToken!, confirmation.ExpiresAt);
			try
			{
				await emailSender.SendAsync(user.Email, message.Subject, message.Body, cancellationToken);
			}
			catch (EmailDeliveryException)
			{
				return (GoogleAuthenticationResult.EmailDeliveryFailed, null);
			}
		}
		var profile = new RegisterUserResponse(user.Id, user.Login, user.Name, user.Surname, user.Email,
			user.BirthDate, user.IsEmailConfirmed, user.CreatedAt);
		return (GoogleAuthenticationResult.Registered, new GoogleRegistrationResponse(profile, authentication, !emailConfirmed));
	}

	private async Task<GoogleIdentity?> ValidateAsync(string idToken, string browserToken, CancellationToken cancellationToken)
	{
		var nonce = challenges.GetNonce(browserToken);
		return nonce is null ? null : await validator.ValidateAsync(idToken, nonce, cancellationToken);
	}

	private bool Consume(string browserToken)
	{
		var nonce = challenges.GetNonce(browserToken);
		return nonce is not null && challenges.TryConsume(browserToken, nonce);
	}
}
