using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.EmailChange;
using ZPI4M.User.Integrations.Smtp;
using ZPI4M.User.Models;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Repositories.UserTokenRepository;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.User.Services.EmailChange;

public sealed class EmailChangeService(
	IUserRepository users,
	IUserTokenRepository tokens,
	UserDbContext dbContext,
	IPasswordHasher<Models.User> passwordHasher,
	IUserTokenGenerator tokenGenerator,
	IEmailSender emailSender,
	IOptions<EmailChangeOptions> options) : IEmailChangeService
{
	public async Task<ChangeEmailResult> RequestAsync(Guid userId, ChangeEmailRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		string rawToken;
		DateTime expiresAt;
		await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
		{
			var user = await users.GetByIdForUpdateAsync(userId, cancellationToken);
			if (user is not { IsActive: true, IsEmailConfirmed: true })
			{
				return ChangeEmailResult.UserUnavailable;
			}

			if (user.GoogleSubject is not null)
			{
				return ChangeEmailResult.GoogleAccountEmailChangeForbidden;
			}

			if (user.PasswordHash is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
			{
				return ChangeEmailResult.InvalidPassword;
			}

			if (user.Email == request.NewEmail)
			{
				return ChangeEmailResult.EmailUnchanged;
			}

			if (await users.ExistsActiveByEmailAsync(request.NewEmail, cancellationToken))
			{
				return ChangeEmailResult.EmailAlreadyInUse;
			}

			var token = await tokens.GetByUserAsync(userId, UserTokenPurpose.EmailChange, cancellationToken);
			var now = DateTime.UtcNow;
			if (token is not null && token.CreatedAt.AddSeconds(options.Value.ResendCooldownSeconds) > now)
			{
				return ChangeEmailResult.CooldownActive;
			}

			rawToken = tokenGenerator.GenerateToken();
			expiresAt = now.AddHours(options.Value.TokenLifetimeHours);
			if (token is null)
			{
				token = new UserToken
				{
					Id = Guid.NewGuid(),
					FkUserId = userId,
					Purpose = UserTokenPurpose.EmailChange
				};
				tokens.Add(token);
			}

			token.TargetEmail = request.NewEmail;
			token.TokenHash = tokenGenerator.HashToken(rawToken);
			token.CreatedAt = now;
			token.ExpiresAt = expiresAt;
			await dbContext.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
		}

		var message = UserEmailMessages.EmailChange(rawToken, expiresAt);
		try
		{
			await emailSender.SendAsync(request.NewEmail, message.Subject, message.Body, cancellationToken);
			return ChangeEmailResult.Accepted;
		}
		catch (EmailDeliveryException)
		{
			return ChangeEmailResult.EmailDeliveryFailed;
		}
	}

	public async Task<ConfirmEmailChangeResult> ConfirmAsync(ConfirmEmailChangeRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var hash = tokenGenerator.HashToken(request.Token);
		var token = await tokens.GetByTokenHashAsync(hash, UserTokenPurpose.EmailChange, cancellationToken);
		if (token?.TargetEmail is null)
		{
			return ConfirmEmailChangeResult.InvalidOrExpiredToken;
		}

		await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
		var user = await users.GetByIdForUpdateAsync(token.FkUserId, cancellationToken);
		if (user is not { IsActive: true, IsEmailConfirmed: true, PasswordHash: not null, GoogleSubject: null } || user.Email == token.TargetEmail)
		{
			return ConfirmEmailChangeResult.InvalidOrExpiredToken;
		}

		if (!await tokens.TryConsumeAsync(token.Id, hash, UserTokenPurpose.EmailChange, DateTime.UtcNow, cancellationToken))
		{
			return ConfirmEmailChangeResult.InvalidOrExpiredToken;
		}

		if (await users.ExistsActiveByEmailAsync(token.TargetEmail, cancellationToken))
		{
			return ConfirmEmailChangeResult.EmailAlreadyInUse;
		}

		try
		{
			if (!await users.TryChangeEmailAsync(user.Id, user.Email, token.TargetEmail, cancellationToken))
			{
				return ConfirmEmailChangeResult.InvalidOrExpiredToken;
			}

			dbContext.Entry(token).State = EntityState.Detached;
			await tokens.RemoveByUserAsync(user.Id, cancellationToken);
			await dbContext.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
		}
		catch (PostgresException exception) when (exception is
		{
			SqlState: PostgresErrorCodes.UniqueViolation,
			ConstraintName: "c_user_unique_email"
		})
		{
			return ConfirmEmailChangeResult.EmailAlreadyInUse;
		}

		return ConfirmEmailChangeResult.Confirmed;
	}
}
