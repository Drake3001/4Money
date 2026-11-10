using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.Models;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Repositories.UserTokenRepository;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.User.Services.Authentication;

public sealed class UserAuthenticationService(
	IUserRepository users,
	IUserTokenRepository tokens,
	UserDbContext dbContext,
	IPasswordHasher<Models.User> passwordHasher,
	IUserTokenGenerator tokenGenerator,
	IUserSessionService sessions) : IUserAuthenticationService
{
	public async Task<(LoginResult Result, AuthenticationResponse? Response)> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var isEmail = request.Login.Contains('@');
		var candidate = isEmail
			? await users.GetActiveByEmailAsync(request.Login, cancellationToken)
			: await users.GetByLoginAsync(request.Login, cancellationToken);
		if (candidate is null)
		{
			return (LoginResult.InvalidCredentials, null);
		}

		await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
		var user = await users.GetByIdForUpdateAsync(candidate.Id, cancellationToken);
		if (user is null || user.PasswordHash is null || (isEmail ? user.Email : user.Login) != request.Login)
		{
			return (LoginResult.InvalidCredentials, null);
		}

		var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
		if (verification == PasswordVerificationResult.Failed)
		{
			return (LoginResult.InvalidCredentials, null);
		}

		if (!user.IsActive)
		{
			return (LoginResult.AccountInactive, null);
		}

		if (!user.IsEmailConfirmed)
		{
			return (LoginResult.InvalidCredentials, null);
		}

		var now = DateTime.UtcNow;
		var response = await sessions.CreateAsync(user.Id, now, cancellationToken);
		var passwordHash = verification == PasswordVerificationResult.SuccessRehashNeeded
			? passwordHasher.HashPassword(user, request.Password) : user.PasswordHash;
		await users.RecordLoginAsync(user.Id, passwordHash, now, cancellationToken);
		await dbContext.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return (LoginResult.LoggedIn, response);
	}

	public async Task<AuthenticationResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var hash = tokenGenerator.HashToken(request.RefreshToken);
		var candidate = await tokens.GetByTokenHashAsync(hash, UserTokenPurpose.RefreshToken, cancellationToken);
		if (candidate is null)
		{
			return null;
		}

		await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
		var user = await users.GetByIdForUpdateAsync(candidate.FkUserId, cancellationToken);
		var now = DateTime.UtcNow;
		if (user is null || !user.IsActive || !user.IsEmailConfirmed ||
			!await tokens.TryConsumeAsync(candidate.Id, hash, UserTokenPurpose.RefreshToken, now, cancellationToken))
		{
			return null;
		}

		dbContext.Entry(candidate).State = EntityState.Detached;
		var rawRefreshToken = tokenGenerator.GenerateToken();
		var session = new UserToken
		{
			Id = candidate.Id,
			FkUserId = user.Id,
			Purpose = UserTokenPurpose.RefreshToken,
			TokenHash = tokenGenerator.HashToken(rawRefreshToken),
			CreatedAt = now,
			ExpiresAt = candidate.ExpiresAt
		};
		tokens.Add(session);
		var response = sessions.CreateResponse(session, rawRefreshToken, now);
		await dbContext.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return response;
	}

	public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
	{
		await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
		await users.GetByIdForUpdateAsync(userId, cancellationToken);
		await tokens.RemoveSessionAsync(userId, sessionId, cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
	{
		return tokens.IsSessionActiveAsync(userId, sessionId, DateTime.UtcNow, cancellationToken);
	}
}
