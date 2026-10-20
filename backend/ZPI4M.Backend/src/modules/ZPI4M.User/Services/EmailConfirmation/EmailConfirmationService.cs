using Microsoft.EntityFrameworkCore;
using ZPI4M.User.Database;
using ZPI4M.User.DTOs.EmailConfirmation;
using ZPI4M.User.Models;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Repositories.UserTokenRepository;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.User.Services.EmailConfirmation;

public sealed class EmailConfirmationService : IEmailConfirmationService
{
	private readonly IUserRepository _users;
	private readonly IUserTokenRepository _tokens;
	private readonly UserDbContext _dbContext;
	private readonly IUserTokenGenerator _tokenGenerator;

	public EmailConfirmationService(
		IUserRepository users,
		IUserTokenRepository tokens,
		UserDbContext dbContext,
		IUserTokenGenerator tokenGenerator)
	{
		_users = users;
		_tokens = tokens;
		_dbContext = dbContext;
		_tokenGenerator = tokenGenerator;
	}

	public async Task<EmailConfirmationResult> ConfirmAsync(
		ConfirmEmailRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var tokenHash = _tokenGenerator.HashToken(request.Token);
		var token = await _tokens.GetByTokenHashAsync(tokenHash, UserTokenPurpose.EmailConfirmation, cancellationToken);
		if (token?.TargetEmail is null)
		{
			return EmailConfirmationResult.InvalidOrExpiredToken;
		}

		await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
		if (!await _tokens.TryConsumeAsync(token.Id, tokenHash, UserTokenPurpose.EmailConfirmation,
			DateTime.UtcNow, cancellationToken))
		{
			return EmailConfirmationResult.InvalidOrExpiredToken;
		}

		if (!await _users.TryConfirmEmailAsync(token.FkUserId, token.TargetEmail, cancellationToken))
		{
			return EmailConfirmationResult.InvalidOrExpiredToken;
		}

		await transaction.CommitAsync(cancellationToken);
		_dbContext.Entry(token).State = EntityState.Detached;
		return EmailConfirmationResult.Confirmed;
	}
}
