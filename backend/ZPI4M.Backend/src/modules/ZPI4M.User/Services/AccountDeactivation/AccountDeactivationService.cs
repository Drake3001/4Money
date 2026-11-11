using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.AccountDeactivation;
using ZPI4M.User.Integrations.Smtp;
using ZPI4M.User.Models;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Repositories.UserTokenRepository;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.User.Services.AccountDeactivation;

public sealed class AccountDeactivationService(
	IUserRepository users,
	IUserTokenRepository tokens,
	UserDbContext dbContext,
	IUserTokenGenerator tokenGenerator,
	IEmailSender emailSender,
	UserEmailMessages emailMessages,
	IOptions<AccountDeactivationOptions> options) : IAccountDeactivationService
{
	public async Task<RequestAccountDeactivationResult> RequestAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		string recipientEmail;
		string rawToken;
		DateTime expiresAt;
		await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
		{
			var user = await users.GetByIdForUpdateAsync(userId, cancellationToken);
			if (user is not { IsActive: true, IsEmailConfirmed: true })
			{
				return RequestAccountDeactivationResult.UserUnavailable;
			}

			var token = await tokens.GetByUserAsync(userId, UserTokenPurpose.AccountDeactivation, cancellationToken);
			var now = DateTime.UtcNow;
			if (token is not null && token.CreatedAt.AddSeconds(options.Value.ResendCooldownSeconds) > now)
			{
				return RequestAccountDeactivationResult.CooldownActive;
			}

			recipientEmail = user.Email;
			rawToken = tokenGenerator.GenerateToken();
			expiresAt = now.AddMinutes(options.Value.TokenLifetimeMinutes);
			if (token is null)
			{
				token = new UserToken
				{
					Id = Guid.NewGuid(),
					FkUserId = userId,
					Purpose = UserTokenPurpose.AccountDeactivation
				};
				tokens.Add(token);
			}

			token.TargetEmail = recipientEmail;
			token.TokenHash = tokenGenerator.HashToken(rawToken);
			token.CreatedAt = now;
			token.ExpiresAt = expiresAt;
			await dbContext.SaveChangesAsync(cancellationToken);
			var message = emailMessages.AccountDeactivation(rawToken, expiresAt);
			try
			{
				await emailSender.SendAsync(recipientEmail, message.Subject, message.Body, cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				return RequestAccountDeactivationResult.Accepted;
			}
			catch (EmailDeliveryException)
			{
				return RequestAccountDeactivationResult.EmailDeliveryFailed;
			}
		}
	}

	public async Task<ConfirmAccountDeactivationResult> ConfirmAsync(ConfirmAccountDeactivationRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var hash = tokenGenerator.HashToken(request.Token);
		var token = await tokens.GetByTokenHashAsync(hash, UserTokenPurpose.AccountDeactivation, cancellationToken);
		if (token?.TargetEmail is null)
		{
			return ConfirmAccountDeactivationResult.InvalidOrExpiredToken;
		}

		await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
		var user = await users.GetByIdForUpdateAsync(token.FkUserId, cancellationToken);
		if (user is not { IsActive: true, IsEmailConfirmed: true } || user.Email != token.TargetEmail)
		{
			return ConfirmAccountDeactivationResult.InvalidOrExpiredToken;
		}

		if (!await tokens.TryConsumeAsync(token.Id, hash, UserTokenPurpose.AccountDeactivation, DateTime.UtcNow, cancellationToken))
		{
			return ConfirmAccountDeactivationResult.InvalidOrExpiredToken;
		}

		if (!await users.TryDeactivateAsync(user.Id, token.TargetEmail, cancellationToken))
		{
			return ConfirmAccountDeactivationResult.InvalidOrExpiredToken;
		}

		dbContext.Entry(token).State = EntityState.Detached;
		await tokens.RemoveByUserAsync(user.Id, cancellationToken);
		await dbContext.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return ConfirmAccountDeactivationResult.Deactivated;
	}
}
