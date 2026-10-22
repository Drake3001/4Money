using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.PasswordReset;
using ZPI4M.User.Models;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Repositories.UserTokenRepository;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.User.Services.PasswordReset;

public sealed class PasswordResetService(
	IUserRepository users,
	IUserTokenRepository tokens,
	UserDbContext dbContext,
	IPasswordHasher<Models.User> passwordHasher,
	IUserTokenGenerator tokenGenerator,
	IPasswordResetMessageSender messageSender,
	IOptions<PasswordResetOptions> options) : IPasswordResetService
{
	public async Task RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var candidate = await users.GetByEmailAsync(request.Email, cancellationToken);
		if (candidate is not { IsActive: true, IsEmailConfirmed: true })
		{
			return;
		}

		string rawToken;
		DateTime expiresAt;
		await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
		{
			var user = await users.GetByIdForUpdateAsync(candidate.Id, cancellationToken);
			if (user is not { IsActive: true, IsEmailConfirmed: true } || user.Email != request.Email)
			{
				return;
			}

			var token = await tokens.GetByUserAsync(user.Id, UserTokenPurpose.PasswordReset, cancellationToken);
			var now = DateTime.UtcNow;
			if (token is not null && token.CreatedAt.AddSeconds(options.Value.ResendCooldownSeconds) > now)
			{
				return;
			}

			rawToken = tokenGenerator.GenerateToken();
			expiresAt = now.AddMinutes(options.Value.TokenLifetimeMinutes);
			if (token is null)
			{
				token = new UserToken
				{
					Id = Guid.NewGuid(),
					FkUserId = user.Id,
					Purpose = UserTokenPurpose.PasswordReset
				};
				tokens.Add(token);
			}

			token.TargetEmail = user.Email;
			token.TokenHash = tokenGenerator.HashToken(rawToken);
			token.CreatedAt = now;
			token.ExpiresAt = expiresAt;
			await dbContext.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
		}

		await messageSender.SendAsync(request.Email, rawToken, expiresAt, cancellationToken);
	}

	public async Task<PasswordResetResult> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var hash = tokenGenerator.HashToken(request.Token);
		var token = await tokens.GetByTokenHashAsync(hash, UserTokenPurpose.PasswordReset, cancellationToken);
		if (token?.TargetEmail is null)
		{
			return PasswordResetResult.InvalidOrExpiredToken;
		}

		await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
		var user = await users.GetByIdForUpdateAsync(token.FkUserId, cancellationToken);
		if (user is not { IsActive: true, IsEmailConfirmed: true } || user.Email != token.TargetEmail)
		{
			return PasswordResetResult.InvalidOrExpiredToken;
		}

		if (!await tokens.TryConsumeAsync(token.Id, hash, UserTokenPurpose.PasswordReset, DateTime.UtcNow, cancellationToken))
		{
			return PasswordResetResult.InvalidOrExpiredToken;
		}

		var passwordHash = passwordHasher.HashPassword(user, request.NewPassword);
		if (!await users.TryResetPasswordAsync(user.Id, token.TargetEmail, passwordHash, cancellationToken))
		{
			return PasswordResetResult.InvalidOrExpiredToken;
		}

		dbContext.Entry(token).State = EntityState.Detached;
		await tokens.RemoveByUserAsync(user.Id, cancellationToken);
		await dbContext.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return PasswordResetResult.Reset;
	}
}
