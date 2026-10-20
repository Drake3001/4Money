using Microsoft.EntityFrameworkCore;
using ZPI4M.User.Database;
using ZPI4M.User.Models;

namespace ZPI4M.User.Repositories.UserTokenRepository;

public class UserTokenRepository : IUserTokenRepository
{
	private readonly UserDbContext _context;

	public UserTokenRepository(UserDbContext context)
	{
		_context = context;
	}

	public void Add(UserToken entity)
	{
		_context.UserTokens.Add(entity);
	}

	public void Remove(UserToken entity)
	{
		_context.UserTokens.Remove(entity);
	}

	public Task<UserToken?> GetByTokenHashAsync(string tokenHash, UserTokenPurpose purpose, CancellationToken cancellationToken = default)
	{
		return _context.UserTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash && t.Purpose == purpose, cancellationToken);
	}

	public async Task<bool> TryConsumeAsync(Guid tokenId, string tokenHash, UserTokenPurpose purpose, DateTime utcNow, CancellationToken cancellationToken = default)
	{
		var affectedRows = await _context.UserTokens
			.Where(t => t.Id == tokenId && t.TokenHash == tokenHash && t.Purpose == purpose && t.ExpiresAt > utcNow)
			.ExecuteDeleteAsync(cancellationToken);
		return affectedRows == 1;
	}

	public Task<UserToken?> GetByUserAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken = default)
	{
		return _context.UserTokens.SingleOrDefaultAsync(t => t.FkUserId == userId && t.Purpose == purpose, cancellationToken);
	}

	public async Task RemoveByUserAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var tokens = await _context.UserTokens.Where(t => t.FkUserId == userId).ToListAsync(cancellationToken);
		_context.UserTokens.RemoveRange(tokens);
	}
}
