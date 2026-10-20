using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
	private readonly IEmailConfirmationMessageSender _messageSender;
	private readonly EmailConfirmationOptions _options;

	public EmailConfirmationService(
		IUserRepository users,
		IUserTokenRepository tokens,
		UserDbContext dbContext,
		IUserTokenGenerator tokenGenerator,
		IEmailConfirmationMessageSender messageSender,
		IOptions<EmailConfirmationOptions> options)
	{
		_users = users;
		_tokens = tokens;
		_dbContext = dbContext;
		_tokenGenerator = tokenGenerator;
		_messageSender = messageSender;
		_options = options.Value;
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
		var user = await _users.GetByIdForUpdateAsync(token.FkUserId, cancellationToken);
		if (user is null || !user.IsActive || user.IsEmailConfirmed || user.Email != token.TargetEmail)
		{
			return EmailConfirmationResult.InvalidOrExpiredToken;
		}

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

	public async Task<ResendEmailConfirmationResult> ResendAsync(
		ResendEmailConfirmationRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var user = await _users.GetByEmailAsync(request.Email, cancellationToken);
		if (user is null || !user.IsActive || user.IsEmailConfirmed)
		{
			return ResendEmailConfirmationResult.Accepted;
		}

		string rawToken;
		DateTime expiresAt;
		await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
		{
			var lockedUser = await _users.GetByIdForUpdateAsync(user.Id, cancellationToken);
			if (lockedUser is null || !lockedUser.IsActive || lockedUser.IsEmailConfirmed || lockedUser.Email != request.Email)
			{
				return ResendEmailConfirmationResult.Accepted;
			}

			var token = await _tokens.GetByUserAsync(user.Id, UserTokenPurpose.EmailConfirmation, cancellationToken);
			var now = DateTime.UtcNow;
			if (token is not null && token.CreatedAt.AddSeconds(_options.ResendCooldownSeconds) > now)
			{
				return ResendEmailConfirmationResult.Accepted;
			}

			rawToken = _tokenGenerator.GenerateToken();
			expiresAt = now.AddHours(_options.TokenLifetimeHours);
			if (token is null)
			{
				token = new UserToken
				{
					Id = Guid.NewGuid(),
					FkUserId = user.Id,
					Purpose = UserTokenPurpose.EmailConfirmation
				};
				_tokens.Add(token);
			}

			token.TokenHash = _tokenGenerator.HashToken(rawToken);
			token.TargetEmail = lockedUser.Email;
			token.CreatedAt = now;
			token.ExpiresAt = expiresAt;
			await _dbContext.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
		}

		var sent = await _messageSender.TrySendAsync(request.Email, rawToken, expiresAt, cancellationToken);
		return sent ? ResendEmailConfirmationResult.Accepted : ResendEmailConfirmationResult.EmailDeliveryFailed;
	}
}
