using Microsoft.EntityFrameworkCore;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Database;

namespace ZPI4M.User.Repositories.UserRepository;

public class UserRepository : IUserRepository
{
	private readonly UserDbContext _context;

	public UserRepository(UserDbContext context)
	{
		_context = context;
	}

	public void Add(Models.User entity)
	{
		_context.Users.Add(entity);
	}

	public void Remove(Models.User entity)
	{
		_context.Users.Remove(entity);
	}

	public Task<Models.User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		return _context.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
	}

	public Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		return _context.Users.AnyAsync(u => u.Id == userId, cancellationToken);
	}

	public Task<Models.User?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		return _context.Users
			.FromSql($"""SELECT * FROM "user"."user" WHERE "_id" = {userId} FOR UPDATE""")
			.AsNoTracking()
			.SingleOrDefaultAsync(cancellationToken);
	}

	public Task<Models.User?> GetByLoginAsync(string login, CancellationToken cancellationToken = default)
	{
		return _context.Users.SingleOrDefaultAsync(u => u.Login == login, cancellationToken);
	}

	public Task<bool> ExistsByLoginAsync(string login, CancellationToken cancellationToken = default)
	{
		return _context.Users.AnyAsync(u => u.Login == login, cancellationToken);
	}

	public Task RecordLoginAsync(Guid userId, string passwordHash, DateTime utcNow, CancellationToken cancellationToken = default)
	{
		return _context.Users.Where(u => u.Id == userId)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(u => u.PasswordHash, passwordHash)
				.SetProperty(u => u.LastLoginTime, utcNow), cancellationToken);
	}

	public Task<Models.User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		return _context.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
	}

	public Task UpdateProfileAsync(Models.User user, CancellationToken cancellationToken = default)
	{
		return _context.Users.Where(u => u.Id == user.Id)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(u => u.Login, user.Login)
				.SetProperty(u => u.Name, user.Name)
				.SetProperty(u => u.Surname, user.Surname)
				.SetProperty(u => u.Phone, user.Phone)
				.SetProperty(u => u.BirthDate, user.BirthDate)
				.SetProperty(u => u.Icon, user.Icon), cancellationToken);
	}

	public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		return _context.Users.AnyAsync(u => u.Email == email, cancellationToken);
	}

	public Task<Models.User?> GetByTinkUserIdAsync(string tinkUserId, CancellationToken cancellationToken = default)
	{
		return _context.Users.SingleOrDefaultAsync(u => u.TinkUserId == tinkUserId, cancellationToken);
	}

	public async Task<bool> TryConfirmEmailAsync(Guid userId, string email, CancellationToken cancellationToken = default)
	{
		var affectedRows = await _context.Users
			.Where(u => u.Id == userId && u.Email == email && u.IsActive && !u.IsEmailConfirmed)
			.ExecuteUpdateAsync(setters => setters.SetProperty(u => u.IsEmailConfirmed, true), cancellationToken);
		return affectedRows == 1;
	}

	public async Task<bool> TryChangeEmailAsync(Guid userId, string currentEmail, string newEmail, CancellationToken cancellationToken = default)
	{
		var affectedRows = await _context.Users
			.Where(u => u.Id == userId && u.Email == currentEmail && u.IsActive && u.IsEmailConfirmed)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(u => u.Email, newEmail)
				.SetProperty(u => u.IsEmailConfirmed, true), cancellationToken);
		return affectedRows == 1;
	}

	public async Task<bool> TryResetPasswordAsync(Guid userId, string email, string passwordHash, CancellationToken cancellationToken = default)
	{
		var affectedRows = await _context.Users
			.Where(u => u.Id == userId && u.Email == email && u.IsActive && u.IsEmailConfirmed)
			.ExecuteUpdateAsync(setters => setters.SetProperty(u => u.PasswordHash, passwordHash), cancellationToken);
		return affectedRows == 1;
	}

	public async Task<PagedResult<Models.User>> GetPageAsync(UserQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		IQueryable<Models.User> users = _context.Users;
		if (!string.IsNullOrWhiteSpace(query.Login))
		{
			var login = query.Login.Trim().ToLowerInvariant();
			users = users.Where(u => u.Login.ToLower().Contains(login));
		}
		if (!string.IsNullOrWhiteSpace(query.Name))
		{
			var name = query.Name.Trim().ToLowerInvariant();
			users = users.Where(u => u.Name.ToLower().Contains(name));
		}
		if (!string.IsNullOrWhiteSpace(query.Surname))
		{
			var surname = query.Surname.Trim().ToLowerInvariant();
			users = users.Where(u => u.Surname.ToLower().Contains(surname));
		}
		if (!string.IsNullOrWhiteSpace(query.Email))
		{
			var email = query.Email.Trim().ToLowerInvariant();
			users = users.Where(u => u.Email != null && u.Email.ToLower().Contains(email));
		}
		if (!string.IsNullOrWhiteSpace(query.Phone))
		{
			var phone = query.Phone.Trim();
			users = users.Where(u => u.Phone == phone);
		}
		if (!string.IsNullOrWhiteSpace(query.TinkUserId))
		{
			var tinkUserId = query.TinkUserId.Trim();
			users = users.Where(u => u.TinkUserId == tinkUserId);
		}
		if (query.IsActive.HasValue)
			users = users.Where(u => u.IsActive == query.IsActive.Value);

		IOrderedQueryable<Models.User> ordered;
		switch (query.SortBy)
		{
			case UserSortBy.Login:
				if (query.Descending)
					ordered = users.OrderByDescending(u => u.Login);
				else
					ordered = users.OrderBy(u => u.Login);
				break;
			case UserSortBy.Name:
				if (query.Descending)
					ordered = users.OrderByDescending(u => u.Name);
				else
					ordered = users.OrderBy(u => u.Name);
				break;
			case UserSortBy.Surname:
				if (query.Descending)
					ordered = users.OrderByDescending(u => u.Surname);
				else
					ordered = users.OrderBy(u => u.Surname);
				break;
			case UserSortBy.Email:
				if (query.Descending)
					ordered = users.OrderByDescending(u => u.Email);
				else
					ordered = users.OrderBy(u => u.Email);
				break;
			case UserSortBy.BirthDate:
				if (query.Descending)
					ordered = users.OrderByDescending(u => u.BirthDate);
				else
					ordered = users.OrderBy(u => u.BirthDate);
				break;
			case UserSortBy.CreatedAt:
				if (query.Descending)
					ordered = users.OrderByDescending(u => u.CreatedAt);
				else
					ordered = users.OrderBy(u => u.CreatedAt);
				break;
			case UserSortBy.LastLoginTime:
				if (query.Descending)
					ordered = users.OrderByDescending(u => u.LastLoginTime);
				else
					ordered = users.OrderBy(u => u.LastLoginTime);
				break;
			case UserSortBy.IsActive:
				if (query.Descending)
					ordered = users.OrderByDescending(u => u.IsActive);
				else
					ordered = users.OrderBy(u => u.IsActive);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(query), "Unknown sort field.");
		}

		int offset = query.GetOffset();
		int totalCount = await users.CountAsync(cancellationToken);
		var items = await ordered.ThenBy(u => u.Id)
			.Skip(offset)
			.Take(query.PageSize)
			.ToListAsync(cancellationToken);

		return new PagedResult<Models.User>(items, totalCount, query.PageNumber, query.PageSize);
	}
}
