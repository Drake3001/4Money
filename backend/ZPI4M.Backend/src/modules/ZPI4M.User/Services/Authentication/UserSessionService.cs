using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.Models;
using ZPI4M.User.Repositories.UserTokenRepository;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.User.Services.Authentication;

public sealed class UserSessionService(
	IUserTokenRepository tokens,
	UserDbContext dbContext,
	IUserTokenGenerator tokenGenerator,
	IAccessTokenGenerator accessTokenGenerator,
	IOptions<AuthenticationOptions> options) : IUserSessionService
{
	public async Task<AuthenticationResponse> CreateAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default)
	{
		if (dbContext.Database.CurrentTransaction is null)
			throw new InvalidOperationException("Session creation requires a user transaction.");
		var previousSession = await tokens.GetByUserAsync(userId, UserTokenPurpose.RefreshToken, cancellationToken);
		if (previousSession is not null)
		{
			await tokens.RemoveSessionAsync(userId, previousSession.Id, cancellationToken);
			dbContext.Entry(previousSession).State = EntityState.Detached;
		}
		var rawRefreshToken = tokenGenerator.GenerateToken();
		var session = new UserToken
		{
			Id = Guid.NewGuid(),
			FkUserId = userId,
			Purpose = UserTokenPurpose.RefreshToken,
			TokenHash = tokenGenerator.HashToken(rawRefreshToken),
			CreatedAt = now,
			ExpiresAt = now.AddDays(options.Value.RefreshTokenLifetimeDays)
		};
		tokens.Add(session);
		return CreateResponse(session, rawRefreshToken, now);
	}

	public AuthenticationResponse CreateResponse(UserToken session, string rawRefreshToken, DateTime now)
	{
		var (accessToken, expiresAt) = accessTokenGenerator.Generate(session.FkUserId, session.Id, now, session.ExpiresAt);
		return new AuthenticationResponse(accessToken, expiresAt, rawRefreshToken, session.ExpiresAt);
	}
}
